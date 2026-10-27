using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YHPCReviewer.AI;

public sealed record ChatTurn(string Role, string Content);

/// <summary>
/// AYT AI'ye (OpenAI uyumlu chat/completions) bağlanır. Yanıtı akış (stream) olarak alır;
/// sunucu akışı desteklemiyorsa tek parça JSON yanıtı da kabul eder.
/// Yalnızca kullanıcı bir işlem başlattığında çalışır.
/// </summary>
public sealed class AiClient(AiSettings settings)
{
    public const string SystemPrompt = """
        Sen YH PC Reviewer adlı bilgisayar sorun giderme aracının asistanı AYT AI'sın.
        Kullanıcı teknik bilgisi olmayan biri olabilir. Sana verilen teşhis raporunu incele ve Türkçe olarak:
        1. En olası 1-3 sorunu önem sırasıyla, sade bir dille açıkla.
        2. Her biri için kullanıcının kendi yapabileceği, güvenli, adım adım çözüm yolu yaz.
        3. Bir teknik servise ne zaman gidilmesi gerektiğini belirt.
        Raporda olmayan şeyleri uydurma; emin değilsen bunu söyle. Kayıt defteri düzenleme, BIOS ayarı değiştirme
        ya da tanımadığı sitelerden program indirme gibi riskli adımlar önerme.
        Kısa başlıklar (##), maddeler ve **kalın** vurgular kullan. Kullanıcı sonradan soru sorarsa aynı rapora göre yanıtla.
        """;

    private HttpClient CreateHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        var key = settings.GetApiKey();
        if (!string.IsNullOrEmpty(key))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("YHPCReviewer/1.0");
        return http;
    }

    /// <summary>Sohbeti gönderir; gelen metin parçalarını <paramref name="onDelta"/> ile anında iletir.</summary>
    public async Task<string> ChatAsync(IReadOnlyList<ChatTurn> conversation, Action<string> onDelta, CancellationToken ct)
    {
        if (!AiSettings.IsValidUrl(settings.Endpoint))
            throw new InvalidOperationException("AYT AI adresi ayarlanmamış. Ayarlar'dan API adresini gir.");

        using var http = CreateHttp(Timeout.InfiniteTimeSpan);
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt } };
        foreach (var t in conversation)
            messages.Add(new JsonObject { ["role"] = t.Role, ["content"] = t.Content });

        var body = new JsonObject { ["messages"] = messages, ["temperature"] = 0.3, ["stream"] = true };
        if (!string.IsNullOrWhiteSpace(settings.Model)) body["model"] = settings.Model;

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ChatUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        // Bağlantı kurulamazsa sonsuza kadar beklememek için yalnızca ilk yanıta zaman aşımı uygula.
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(DescribeError((int)response.StatusCode, err));
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
        {
            // Sunucu akışı desteklemiyor: tek parça yanıt.
            var json = await response.Content.ReadAsStringAsync(ct);
            var text = ExtractText(json) ?? throw new InvalidOperationException("AYT AI yanıtı anlaşılamadı: " + Shorten(json));
            onDelta(text);
            return text;
        }

        var sb = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            var delta = ExtractDelta(data);
            if (string.IsNullOrEmpty(delta)) continue;
            sb.Append(delta);
            onDelta(delta);
        }

        if (sb.Length == 0) throw new InvalidOperationException("AYT AI boş bir yanıt döndürdü.");
        return sb.ToString();
    }

    /// <summary>Bağlantıyı küçük bir istekle dener ve kullanıcıya gösterilecek sonucu döndürür.</summary>
    public async Task<string> TestAsync(CancellationToken ct)
    {
        using var http = CreateHttp(TimeSpan.FromSeconds(30));
        var body = new JsonObject
        {
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Sadece 'tamam' yaz." } },
            ["max_tokens"] = 5
        };
        if (!string.IsNullOrWhiteSpace(settings.Model)) body["model"] = settings.Model;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await http.PostAsync(settings.ChatUrl,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(DescribeError((int)response.StatusCode, text));
        if (ExtractText(text) is null)
            throw new InvalidOperationException("Sunucu yanıt verdi ama biçimi OpenAI uyumlu değil.");
        return $"Bağlantı başarılı ({sw.ElapsedMilliseconds} ms).";
    }

    /// <summary>GET /models ile kullanılabilir modelleri listeler.</summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        using var http = CreateHttp(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(settings.ModelsUrl, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(DescribeError((int)response.StatusCode, text));

        var node = JsonNode.Parse(text);
        var items = node?["data"] as JsonArray ?? node as JsonArray ?? node?["models"] as JsonArray;
        return items?
            .Select(m => Str(m?["id"]) ?? Str(m?["name"]) ?? Str(m))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .OrderBy(s => s)
            .ToList() ?? [];
    }

    private static string DescribeError(int status, string body) => status switch
    {
        401 or 403 => "API anahtarı geçersiz ya da yetkisiz (" + status + ").",
        404 => "Adres bulunamadı (404). API adresinin doğru olduğundan emin ol (ör. https://…/v1).",
        429 => "Çok fazla istek ya da kota doldu (429). Biraz sonra tekrar dene.",
        >= 500 => $"AYT AI sunucusunda bir hata oluştu ({status}).",
        _ => $"İstek başarısız oldu ({status}): {Shorten(body)}"
    };

    private static string? ExtractDelta(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            if (obj["choices"] is JsonArray { Count: > 0 } choices && choices[0] is JsonObject c)
                return Str(c["delta"] is JsonObject d ? d["content"] : null) ?? Str(c["text"]);
            return Str(obj["text"]) ?? Str(obj["content"]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>OpenAI tarzı "choices[0].message.content" ya da basit "{ text | content | answer }" yanıtlarını kabul eder.</summary>
    private static string? ExtractText(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return null; }
        if (node is not JsonObject obj) return null;

        if (obj["choices"] is JsonArray { Count: > 0 } choices && choices[0] is JsonObject first)
            return Str(first["message"] is JsonObject m ? m["content"] : null) ?? Str(first["text"]);

        if (obj["content"] is JsonArray { Count: > 0 } parts && parts[0] is JsonObject part)
            return Str(part["text"]);

        return Str(obj["text"]) ?? Str(obj["answer"]) ?? Str(obj["content"]);
    }

    private static string? Str(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Shorten(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
