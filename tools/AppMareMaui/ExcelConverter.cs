using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using nietras.SeparatedValues;


namespace AppMare_Maui.Services;

public static class GoogleSheetsConverter
{
    private const string CN = nameof(GoogleSheetsConverter);
  [MethodImpl(MethodImplOptions.NoInlining)]
  public static  object ConvertJsonElement(JsonElement elem)
{
    switch (elem.ValueKind)
    {
        case JsonValueKind.Object:
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in elem.EnumerateObject())
            {
                dict[prop.Name] = ConvertJsonElement(prop.Value);
            }
            return dict;
        }

        case JsonValueKind.Array:
        {
            var list = new List<object>();
            foreach (var item in elem.EnumerateArray())
            {
                list.Add(ConvertJsonElement(item));
            }
            return list;
        }

        case JsonValueKind.String:
        {
            var str = elem.GetString();

            // special case: string that itself contains JSON array/object
            if (!string.IsNullOrWhiteSpace(str) &&
                (str.TrimStart().StartsWith("[") || str.TrimStart().StartsWith("{")))
            {
                        try
                        {
                            using var innerDoc = JsonDocument.Parse(str);
                            if (innerDoc.RootElement.ValueKind == JsonValueKind.Array ||
                                innerDoc.RootElement.ValueKind == JsonValueKind.Object)
                            {


                                return ConvertJsonElement(innerDoc.RootElement);
                            }
                    else {
                        return str;
                    }
                }
                        catch
                        {
                            // not valid JSON, just return raw string
                        }
            }

            return str;
        }

        case JsonValueKind.Number:
          
            return elem.GetDouble();

        case JsonValueKind.True:
            return true;

        case JsonValueKind.False:
            return false;

        case JsonValueKind.Null:
            return null;

        default:
            return elem.GetRawText(); // fallback
    }
}
    /// <summary>
    /// Convert a Google Sheets share link to a direct CSV download link.
    /// </summary>
    /// <param name="shareLink">The Google Sheets share URL</param>
    /// <param name="sheetName">Name of the specific sheet tab to export (optional)</param>
    /// <param name="gid">Sheet ID (gid) of the specific tab to export (optional)</param>
    /// <returns>Direct CSV download link</returns>
    /// <exception cref="ArgumentException">Thrown when the link is not a valid Google Sheets URL</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public  static T GetFromUrl<T>(string url)
    {
        var httpClient = new HttpClient();
        var response = httpClient.GetAsync(url).Result;
        var content = response.Content.ReadAsStringAsync().Result;
        using var reader = nietras.SeparatedValues.Sep.Reader().FromText(content);
        var records = new List<Dictionary<string, object>>();

        foreach (var row in reader)
        {
            var record = new Dictionary<string, object>();

            for (var i = 0; i < row.ColCount; i++)
            {
                var columnName = reader.Header.ColNames[i];  // ✅ safe to index
                var value = row[i].ToString();
                record[columnName] = value ?? string.Empty;
            }

            records.Add(record);
        }
        var json = JsonSerializer.Serialize(records);

        return DeserializeWithInfo<T>(json);
    }
        [MethodImpl(MethodImplOptions.NoInlining)]
    public  static T  LoadCsv<T>(string content)
    {

        var readerr = nietras.SeparatedValues.Sep.Reader();
                          using var reader =readerr.FromText(content);
                                    var records = new List<Dictionary<string, object>>();

        foreach (var row in reader)
        {
            var record = new Dictionary<string, object>();

            for (var i = 0; i < row.ColCount; i++)
            {
                var columnName = reader.Header.ColNames[i];  // ✅ safe to index
                var value = row[i].ToString();
                record[columnName] = value ?? string.Empty;
            }

            records.Add(record);
        }
        var json = JsonSerializer.Serialize(records);

        return DeserializeWithInfo<T>(json);
    }

    static T DeserializeWithInfo<T>(string json)
    {
        var ctx = AppSerializationContext.Current;
        if (ctx != null)
        {
            var ti = ctx.GetTypeInfo(typeof(T));
            if (ti != null) return (T)System.Text.Json.JsonSerializer.Deserialize(json, ti);
        }
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }
    public static string ConvertToCSVLink(string shareLink, string sheetName = null, int? gid = null)
    {
        if (string.IsNullOrWhiteSpace(shareLink))
            throw new ArgumentException("Share link cannot be null or empty", nameof(shareLink));

        // Extract the spreadsheet ID from various Google Sheets URL formats
        string[] patterns = {
            @"/spreadsheets/d/([a-zA-Z0-9-_]+)",  // Standard format
            @"[?&]id=([a-zA-Z0-9-_]+)",           // Query parameter format
        };

        string spreadsheetId = null;
        foreach (string pattern in patterns)
        {
            Match match = Regex.Match(shareLink, pattern);
            if (match.Success)
            {
                spreadsheetId = match.Groups[1].Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(spreadsheetId))
            throw new ArgumentException("Invalid Google Sheets URL. Could not extract spreadsheet ID.", nameof(shareLink));

        // Build the CSV export URL
        string csvUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv";

        // Add gid parameter if specified
        if (gid.HasValue)
        {
            csvUrl += $"&gid={gid.Value}";
        }
        // If no gid but sheet name is provided, try to extract gid from original URL
        else if (!string.IsNullOrEmpty(sheetName))
        {
            // Note: This is a limitation - we can't convert sheet name to gid without API access
            // The user would need to provide the gid directly
            throw new ArgumentException("When specifying a sheet name, you must also provide the gid parameter. " +
                                      "You can find the gid in the original URL after '#gid=' or by inspecting the sheet tab URL.");
        }
        else
        {
            // Try to extract gid from the original URL if present
            Match gidMatch = Regex.Match(shareLink, @"[#&]gid=([0-9]+)");
            if (gidMatch.Success)
            {
                csvUrl += $"&gid={gidMatch.Groups[1].Value}";
            }
        }

        return csvUrl;
    }

    /// <summary>
    /// Extract spreadsheet ID from a Google Sheets URL
    /// </summary>
    /// <param name="shareLink">The Google Sheets share URL</param>
    /// <returns>The spreadsheet ID</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ExtractSpreadsheetId(string shareLink)
    {
        if (string.IsNullOrWhiteSpace(shareLink))
            return null;

        string[] patterns = {
            @"/spreadsheets/d/([a-zA-Z0-9-_]+)",
            @"[?&]id=([a-zA-Z0-9-_]+)",
        };

        foreach (string pattern in patterns)
        {
            Match match = Regex.Match(shareLink, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Extract gid (sheet ID) from a Google Sheets URL
    /// </summary>
    /// <param name="shareLink">The Google Sheets share URL</param>
    /// <returns>The gid if found, otherwise null</returns>
    public static int? ExtractGid(string shareLink)
    {
        if (string.IsNullOrWhiteSpace(shareLink))
            return null;

        Match gidMatch = Regex.Match(shareLink, @"[#&]gid=([0-9]+)");
        if (gidMatch.Success && int.TryParse(gidMatch.Groups[1].Value, out int gid))
        {
            return gid;
        }

        return null;
    }
}