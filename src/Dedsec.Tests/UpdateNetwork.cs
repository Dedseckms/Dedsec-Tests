using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Dedsec.Tests;
public sealed record PackageProgress(long Received,long Total,double BytesPerSecond,int Attempt)
{
    public double Percent=>Total>0?Math.Min(100,100d*Received/Total):0;
    public string Text=>$"{Percent:F0}% · {Received/1048576d:F1} / {Total/1048576d:F1} MiB · {BytesPerSecond/1048576:F1} MiB/с"+(BytesPerSecond>0?$" · осталось около {TimeSpan.FromSeconds(Math.Max(0,(Total-Received)/BytesPerSecond)):mm\\:ss}":"");
}
public static class UpdateNetwork
{
    public static HttpClient Create()
    {
        // Keep Windows/system proxy and certificate validation. Never accept untrusted TLS certificates.
        var client=new HttpClient(new SocketsHttpHandler{ConnectTimeout=TimeSpan.FromSeconds(15),PooledConnectionLifetime=TimeSpan.FromMinutes(2)}){Timeout=Timeout.InfiniteTimeSpan,DefaultRequestVersion=HttpVersion.Version11,DefaultVersionPolicy=HttpVersionPolicy.RequestVersionExact};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DedsecTests/"+typeof(Updates).Assembly.GetName().Version?.ToString(3));return client;
    }
    private static bool Retryable(Exception error)=>error is TimeoutException||error is HttpRequestException http&&(http.StatusCode==null||(int)http.StatusCode==408||(int)http.StatusCode==429||(int)http.StatusCode>=500)||error is IOException;
    private static async Task<int> Read(Stream stream,Memory<byte> buffer,CancellationToken token)
    {
        using var idle=CancellationTokenSource.CreateLinkedTokenSource(token);idle.CancelAfter(TimeSpan.FromSeconds(20));
        try{return await stream.ReadAsync(buffer,idle.Token);}catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TimeoutException("Сервер не передавал данные в течение 20 секунд.");}
    }
    private static async Task<HttpResponseMessage> Open(HttpClient client,string url,CancellationToken token)
    {
        using var headers=CancellationTokenSource.CreateLinkedTokenSource(token);headers.CancelAfter(TimeSpan.FromSeconds(25));
        try{var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,headers.Token);try{response.EnsureSuccessStatusCode();return response;}catch{response.Dispose();throw;}}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TimeoutException("Сервер не ответил в течение 25 секунд.");}
    }
    public static async Task<byte[]> Metadata(HttpClient client,string url,int limit,CancellationToken token,IProgress<string>? status=null)
    {
        for(int attempt=1;;attempt++)try
        {
            using var response=await Open(client,url,token);if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Метаданные обновления слишком велики");using var stream=await response.Content.ReadAsStreamAsync(token);using var saved=new MemoryStream();byte[] buffer=new byte[8192];int count;
            while((count=await Read(stream,buffer,token))>0){if(saved.Length+count>limit)throw new InvalidDataException("Метаданные обновления слишком велики");saved.Write(buffer,0,count);}return saved.ToArray();
        }
        catch(Exception error)when(attempt<3&&Retryable(error)&&!token.IsCancellationRequested){status?.Report($"Повтор подключения {attempt+1} из 3 · {new Uri(url).Host}");await Task.Delay(TimeSpan.FromSeconds(attempt),token);}
    }
    public static async Task Download(HttpClient client,string url,string destination,long size,IProgress<PackageProgress> progress,CancellationToken cancellation,IProgress<string>? status=null)
    {
        using var duration=CancellationTokenSource.CreateLinkedTokenSource(cancellation);duration.CancelAfter(TimeSpan.FromMinutes(30));var token=duration.Token;
        try
        {
            for(int attempt=1;;attempt++)try
            {
                progress.Report(new(0,size,0,attempt));using var response=await Open(client,url,token);if(response.Content.Headers.ContentLength is long declared&&declared!=size)throw new InvalidDataException("Размер ответа сервера не совпадает с подписанным размером пакета.");
                using var input=await response.Content.ReadAsStreamAsync(token);using var output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None,65536,true);byte[] buffer=new byte[65536];long total=0;var clock=Stopwatch.StartNew();double last=0;int count;
                while((count=await Read(input,buffer,token))>0){total+=count;if(total>size)throw new InvalidDataException("Скачивание превысило подписанный размер пакета.");await output.WriteAsync(buffer.AsMemory(0,count),token);if(clock.Elapsed.TotalSeconds-last>=.15||total==size){last=clock.Elapsed.TotalSeconds;progress.Report(new(total,size,total/Math.Max(.001,last),attempt));}}
                if(total!=size)throw new IOException("Соединение завершилось до загрузки всего пакета.");return;
            }
            catch(Exception error)when(attempt<3&&Retryable(error)&&!token.IsCancellationRequested){status?.Report($"Соединение прервалось. Попытка {attempt+1} из 3; загрузка начнётся заново.");await Task.Delay(TimeSpan.FromSeconds(attempt),token);}
        }
        catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){throw new TimeoutException("Загрузка превысила 30 минут. Повтори попытку или скачай установщик через браузер.");}
    }
    public static string Explain(Exception error)
    {
        var errors=new System.Collections.Generic.List<Exception>();for(Exception? item=error;item!=null&&errors.Count<12;item=item.InnerException)errors.Add(item);var chain=errors.ToArray();
        // Build the chain without losing the useful Windows/Schannel inner error.
        return chain.Any(e=>e is System.Security.Authentication.AuthenticationException||e is HttpRequestException h&&h.HttpRequestError==HttpRequestError.SecureConnectionError)?"Не удалось установить защищённое соединение с GitHub. Проверь дату и время Windows, системные обновления сертификатов и настройки HTTPS в прокси/антивирусе. Можно скачать установщик через браузер. Проверка сертификатов не отключается.":chain.Any(e=>e is HttpRequestException h&&h.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)?"GitHub ограничил запросы. Подожди и повтори проверку либо открой страницу скачивания.":chain.Any(e=>e is TimeoutException)?chain.First(e=>e is TimeoutException).Message:chain.First().Message;
    }
    public static string Details(Exception error){var lines=new System.Collections.Generic.List<string>();for(Exception? item=error;item!=null;item=item.InnerException)lines.Add(item.GetType().Name+": "+item.Message);return string.Join("\n",lines);}
}
