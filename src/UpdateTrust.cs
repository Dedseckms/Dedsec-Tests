using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
namespace Dedsec.Update;
public sealed record UpdateManifest(string Version,string Url,string Sha256,long Size,string Architecture);
public static class Trust
{
    public static UpdateManifest Verify(byte[] bytes,byte[] signature)
    {
        using var resource=typeof(Trust).Assembly.GetManifestResourceStream("UpdatePublicKey.pem")??throw new InvalidDataException("Missing update key");using var reader=new StreamReader(resource);using var rsa=RSA.Create();rsa.ImportFromPem(reader.ReadToEnd());
        if(bytes.Length>16384||!rsa.VerifyData(bytes,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("Неверная подпись обновления");
        var manifest=JsonSerializer.Deserialize<UpdateManifest>(bytes,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Invalid update manifest");
        var uri=new Uri(manifest.Url);if(uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/Dedseckms/Dedsec-Tests/releases/download/",StringComparison.Ordinal)||uri.Query.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("Invalid update source");
        if(!System.Version.TryParse(manifest.Version,out _)||manifest.Size<1||manifest.Size>400_000_000||manifest.Sha256.Length!=64||!manifest.Sha256.All(Uri.IsHexDigit)||manifest.Architecture is not ("x64" or "x86"))throw new InvalidDataException("Invalid update metadata");
        return manifest;
    }
    public static void VerifyPackage(string file,UpdateManifest manifest)
    {using var stream=File.OpenRead(file);if(stream.Length!=manifest.Size||!Convert.ToHexString(SHA256.HashData(stream)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Пакет повреждён: SHA-256 или размер не совпадает");}
}
