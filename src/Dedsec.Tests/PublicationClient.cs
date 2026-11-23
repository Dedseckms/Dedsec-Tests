using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
namespace Dedsec.Tests;
public static class PublicationClient
{
    // The caller must save this key before sending, so a lost response is recoverable.
    public static async Task<string> Send(HttpClient http,Report report)
    {
        if(!report.Completed||report.Profile!="standard"||report.DeleteToken==null)throw new InvalidOperationException("Нужен завершённый стандартный отчёт и сохранённый ключ удаления.");
        var challenge=await http.GetFromJsonAsync<JsonElement>("api/challenge");
        var data=JsonSerializer.SerializeToNode(report.PublicPayload(),Json.Options)!;data["nonce"]=challenge.GetProperty("nonce").GetString();
        var bytes=JsonSerializer.SerializeToUtf8Bytes(data,Json.Options);
        if(bytes.Length>32768)throw new IOException("Публикация превышает допустимые 32 KiB. Локальный отчёт сохранён.");
        // JsonContent/PostAsJsonAsync streams with chunked transfer on HTTP/1.1.
        // Our bounded registry requires Content-Length before reading the body.
        using var request=new HttpRequestMessage(HttpMethod.Post,"api/results");
        request.Headers.Add("X-Deletion-Key",report.DeleteToken);
        request.Content=new ByteArrayContent(bytes);request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json"){CharSet="utf-8"};
        using var response=await http.SendAsync(request);string text=await response.Content.ReadAsStringAsync();
        if(!response.IsSuccessStatusCode)throw new IOException($"Сервер: HTTP {(int)response.StatusCode}. {text[..Math.Min(text.Length,600)]}");
        using var answer=JsonDocument.Parse(text);string id=answer.RootElement.GetProperty("id").GetString()??"";
        if(id.Length!=24||!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{24}$")||answer.RootElement.GetProperty("delete_token").GetString()!=report.DeleteToken)throw new IOException("Некорректное подтверждение публикации. Повторите отправку с сохранённым ключом.");
        // Confirm that the accepted result really is available before claiming success.
        using var readBack=await http.GetAsync("api/results/"+id);readBack.EnsureSuccessStatusCode();
        using var result=JsonDocument.Parse(await readBack.Content.ReadAsStringAsync());
        if(result.RootElement.GetProperty("id").GetString()!=id)throw new IOException("Сервер не подтвердил сохранённый результат.");
        foreach(string field in new[]{"suite_version","app_version","profile","system","measurements"})
            if(!System.Text.Json.Nodes.JsonNode.DeepEquals(data[field],System.Text.Json.Nodes.JsonNode.Parse(result.RootElement.GetProperty(field).GetRawText())))throw new IOException("Сохранённые данные отличаются от отправленного отчёта. Повторите проверку публикации.");
        return "https://tests.dedseckms.ru/result/"+id;
    }
}
