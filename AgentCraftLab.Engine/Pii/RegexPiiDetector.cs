using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Pii;

/// <summary>
/// 基於 Regex + Checksum + Context-aware 的 PII 偵測器。
/// 支援 6 個地區（Global/TW/JP/KR/US/UK）共 30+ 條規則，零外部依賴。
/// </summary>
public sealed class RegexPiiDetector : IPiiDetector
{
    /// <summary>Checksum 通過時的信賴度加成。</summary>
    private const double ChecksumPassBoost = 0.25;

    /// <summary>Checksum 失敗時的信賴度扣減。</summary>
    private const double ChecksumFailPenalty = 0.30;

    /// <summary>前後文關鍵字命中時的信賴度加成。</summary>
    private const double ContextKeywordBoost = 0.15;

    /// <summary>前後文搜尋視窗（匹配位置前後各幾個字元）。</summary>
    private const int ContextWindowChars = 50;

    private readonly IReadOnlyList<PiiRule> _rules;

    /// <summary>內部規則定義。</summary>
    /// <remarks>Validator 預設接收純數字（ExtractDigits 結果），設定 ValidateWithOriginalText=true 則接收原始 matched text。</remarks>
    private sealed record PiiRule(
        PiiLocale Locale,
        PiiEntityType Type,
        string Label,
        Regex Pattern,
        double BaseConfidence,
        Func<string, bool>? Validator,
        string[]? ContextKeywords,
        bool ValidateWithOriginalText = false);

    /// <summary>
    /// 建立偵測器，可指定啟用的地區和自訂規則。
    /// </summary>
    /// <param name="enabledLocales">啟用的地區（null = 全部啟用）。</param>
    /// <param name="customRules">自訂規則（key=Label, value=regex pattern）。</param>
    /// <param name="logger">可選的日誌記錄器，用於記錄無效自訂規則等警告。</param>
    public RegexPiiDetector(
        IEnumerable<PiiLocale>? enabledLocales = null,
        Dictionary<string, string>? customRules = null,
        ILogger? logger = null)
    {
        var localeSet = enabledLocales is not null
            ? new HashSet<PiiLocale>(enabledLocales)
            : null; // null = all

        var rules = new List<PiiRule>();

        foreach (var rule in AllRules)
        {
            if (localeSet is null || localeSet.Contains(rule.Locale))
            {
                rules.Add(rule);
            }
        }

        if (customRules is not null)
        {
            foreach (var (label, pattern) in customRules)
            {
                try
                {
                    rules.Add(new PiiRule(
                        PiiLocale.Global, PiiEntityType.Custom, label,
                        new Regex(pattern, RegexOptions.Compiled),
                        0.80, null, null));
                }
                catch (ArgumentException ex)
                {
                    logger?.LogWarning("[PII] Invalid custom regex rule '{RuleName}': {Message}, skipped", label, ex.Message);
                }
            }
        }

        _rules = rules;
    }

    /// <summary>
    /// 從前端 config dictionary 建立偵測器（向下相容）。
    /// 支援的 key：locales, maskTypes（舊格式）, customRules。
    /// </summary>
    /// <param name="config">前端 config dictionary。</param>
    /// <param name="logger">可選的日誌記錄器。</param>
    public static RegexPiiDetector FromConfig(Dictionary<string, string>? config, ILogger? logger = null)
    {
        if (config is null || config.Count == 0)
        {
            return new RegexPiiDetector(logger: logger);
        }

        // 解析 locales
        List<PiiLocale>? locales = null;
        if (config.TryGetValue("locales", out var localeStr) && !string.IsNullOrWhiteSpace(localeStr))
        {
            locales = [];
            foreach (var s in localeStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<PiiLocale>(s, ignoreCase: true, out var locale))
                {
                    locales.Add(locale);
                }
            }
        }

        // 舊格式 maskTypes → 映射到 locale（向下相容）
        if (locales is null && config.TryGetValue("maskTypes", out var maskTypes) && !string.IsNullOrWhiteSpace(maskTypes))
        {
            locales = [PiiLocale.Global, PiiLocale.TW]; // 舊格式預設
        }

        // 解析自訂規則
        Dictionary<string, string>? custom = null;
        if (config.TryGetValue("customRules", out var rulesStr) && !string.IsNullOrWhiteSpace(rulesStr))
        {
            custom = [];
            foreach (var entry in rulesStr.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    custom[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }

        return new RegexPiiDetector(locales, custom, logger);
    }

    /// <inheritdoc/>
    public IReadOnlyList<PiiEntity> Detect(string text, double confidenceThreshold = 0.5)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var candidates = MatchAllRules(text, confidenceThreshold);
        var resolved = ResolveOverlaps(candidates);

        // 依 Start 降序排列（右到左安全替換）
        resolved.Sort((a, b) => b.Start.CompareTo(a.Start));

        return resolved;
    }

    /// <summary>對所有規則執行 pattern matching，回傳初步偵測結果。</summary>
    private List<PiiEntity> MatchAllRules(string text, double confidenceThreshold)
    {
        var candidates = new List<PiiEntity>();

        foreach (var rule in _rules)
        {
            foreach (Match match in rule.Pattern.Matches(text))
            {
                if (!match.Success)
                {
                    continue;
                }

                var confidence = AdjustConfidence(rule, match, text);

                if (confidence >= confidenceThreshold)
                {
                    candidates.Add(new PiiEntity
                    {
                        Type = rule.Type,
                        Locale = rule.Locale,
                        Label = rule.Label,
                        Start = match.Index,
                        Length = match.Length,
                        Text = match.Value,
                        Confidence = confidence,
                    });
                }
            }
        }

        return candidates;
    }

    /// <summary>根據 Checksum 驗證和前後文關鍵字調整信賴度。</summary>
    private static double AdjustConfidence(PiiRule rule, Match match, string text)
    {
        var confidence = rule.BaseConfidence;

        // Checksum 驗證
        if (rule.Validator is not null)
        {
            var input = rule.ValidateWithOriginalText ? match.Value : ExtractDigits(match.Value);
            if (rule.Validator(input))
            {
                confidence = Math.Min(1.0, confidence + ChecksumPassBoost);
            }
            else
            {
                confidence = Math.Max(0.0, confidence - ChecksumFailPenalty);
            }
        }

        // Context-aware 加權
        if (rule.ContextKeywords is { Length: > 0 })
        {
            var contextStart = Math.Max(0, match.Index - ContextWindowChars);
            var contextEnd = Math.Min(text.Length, match.Index + match.Length + ContextWindowChars);
            var context = text[contextStart..contextEnd];

            foreach (var keyword in rule.ContextKeywords)
            {
                if (context.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    confidence = Math.Min(1.0, confidence + ContextKeywordBoost);
                    break;
                }
            }
        }

        return confidence;
    }

    /// <summary>解析重疊的偵測結果，保留信賴度最高者。</summary>
    private static List<PiiEntity> ResolveOverlaps(List<PiiEntity> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        // 依 Start 升序、Confidence 降序排列
        candidates.Sort((a, b) =>
        {
            var cmp = a.Start.CompareTo(b.Start);
            return cmp != 0 ? cmp : b.Confidence.CompareTo(a.Confidence);
        });

        var result = new List<PiiEntity> { candidates[0] };

        for (var i = 1; i < candidates.Count; i++)
        {
            var prev = result[^1];
            var curr = candidates[i];

            // 是否重疊
            if (curr.Start < prev.Start + prev.Length)
            {
                // 保留 confidence 較高者
                if (curr.Confidence > prev.Confidence)
                {
                    result[^1] = curr;
                }
            }
            else
            {
                result.Add(curr);
            }
        }

        return result;
    }

    /// <summary>從字串中提取純數字（用於 Checksum 驗證）。</summary>
    private static string ExtractDigits(string text)
    {
        return new string(text.Where(char.IsDigit).ToArray());
    }

    // ─── Checksum 驗證方法 ────────────────────────────────────────

    /// <summary>Luhn 算法驗證（信用卡）。</summary>
    private static bool LuhnCheck(string digits)
    {
        if (digits.Length < 12 || digits.Length > 19)
        {
            return false;
        }

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    /// <summary>IBAN mod97 驗證。</summary>
    private static bool Mod97Check(string raw)
    {
        // 移除空白，取英數
        var iban = new string(raw.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpperInvariant();
        if (iban.Length < 15 || iban.Length > 34)
        {
            return false;
        }

        // 移動前 4 碼到最後
        var rearranged = iban[4..] + iban[..4];

        // 字母轉數字（A=10, B=11, ...）
        var numeric = string.Concat(rearranged.Select(c =>
            char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));

        // mod97 計算
        var remainder = 0;
        foreach (var ch in numeric)
        {
            remainder = (remainder * 10 + (ch - '0')) % 97;
        }
        return remainder == 1;
    }

    /// <summary>台灣身分證字號校驗。</summary>
    private static bool TwIdCheck(string raw)
    {
        if (raw.Length != 10 || !char.IsLetter(raw[0]))
        {
            return false;
        }

        // 英文字母對應數值
        var letterValues = new Dictionary<char, int>
        {
            ['A'] = 10, ['B'] = 11, ['C'] = 12, ['D'] = 13, ['E'] = 14,
            ['F'] = 15, ['G'] = 16, ['H'] = 17, ['I'] = 34, ['J'] = 18,
            ['K'] = 19, ['L'] = 20, ['M'] = 21, ['N'] = 22, ['O'] = 35,
            ['P'] = 23, ['Q'] = 24, ['R'] = 25, ['S'] = 26, ['T'] = 27,
            ['U'] = 28, ['V'] = 29, ['W'] = 32, ['X'] = 30, ['Y'] = 31,
            ['Z'] = 33,
        };

        var letter = char.ToUpperInvariant(raw[0]);
        if (!letterValues.TryGetValue(letter, out var letterVal))
        {
            return false;
        }

        var sum = (letterVal / 10) + (letterVal % 10) * 9;
        int[] weights = [8, 7, 6, 5, 4, 3, 2, 1];
        for (var i = 0; i < 8; i++)
        {
            if (!char.IsDigit(raw[i + 1]))
            {
                return false;
            }
            sum += (raw[i + 1] - '0') * weights[i];
        }

        if (!char.IsDigit(raw[9]))
        {
            return false;
        }
        sum += raw[9] - '0';

        return sum % 10 == 0;
    }

    /// <summary>台灣統一編號校驗。</summary>
    private static bool TwBusinessIdCheck(string digits)
    {
        if (digits.Length != 8)
        {
            return false;
        }

        int[] weights = [1, 2, 1, 2, 1, 2, 4, 1];
        var sum = 0;
        for (var i = 0; i < 8; i++)
        {
            var product = (digits[i] - '0') * weights[i];
            sum += product / 10 + product % 10;
        }

        if (sum % 5 == 0)
        {
            return true;
        }

        // 第 7 碼為 7 時允許 sum % 5 == 0 或 (sum+1) % 5 == 0
        if (digits[6] == '7' && (sum + 1) % 5 == 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>日本 My Number 校驗。</summary>
    private static bool JpMyNumberCheck(string digits)
    {
        if (digits.Length != 12)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 11; i++)
        {
            var weight = i < 6 ? 6 - i + 1 : 12 - i + 7;
            sum += (digits[i] - '0') * weight;
        }

        var checkDigit = 11 - (sum % 11);
        if (checkDigit >= 10)
        {
            checkDigit = 0;
        }

        return (digits[11] - '0') == checkDigit;
    }

    /// <summary>韓國住民登錄番號校驗。</summary>
    private static bool KrRrnCheck(string digits)
    {
        if (digits.Length != 13)
        {
            return false;
        }

        int[] weights = [2, 3, 4, 5, 6, 7, 8, 9, 2, 3, 4, 5];
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (digits[i] - '0') * weights[i];
        }

        var check = (11 - (sum % 11)) % 10;
        return (digits[12] - '0') == check;
    }

    /// <summary>英國 NHS Number 校驗。</summary>
    private static bool UkNhsCheck(string digits)
    {
        if (digits.Length != 10)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }

        var check = 11 - (sum % 11);
        if (check == 11)
        {
            check = 0;
        }
        if (check == 10)
        {
            return false; // 無效
        }

        return (digits[9] - '0') == check;
    }

    // ─── 規則定義 ────────────────────────────────────────

    private static Regex R(string pattern) => new(pattern, RegexOptions.Compiled);

    private static readonly IReadOnlyList<PiiRule> AllRules =
    [
        // ─── Global ───
        new(PiiLocale.Global, PiiEntityType.Email, "Email",
            R(@"[\w.+-]+@[\w-]+\.[\w.-]+"), 0.95, null, null),

        new(PiiLocale.Global, PiiEntityType.InternationalPhone, "International Phone",
            R(@"\+\d{1,3}[\s.-]?\(?\d{1,4}\)?[\s.-]?\d{2,4}[\s.-]?\d{2,4}[\s.-]?\d{0,4}"),
            0.75, null, ["phone", "tel", "call", "mobile", "cell", "電話", "手機", "携帯"]),

        new(PiiLocale.Global, PiiEntityType.CreditCard, "Credit Card",
            R(@"(?<!\d)\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{1,7}(?!\d)"),
            0.70, LuhnCheck, ["card", "credit", "visa", "master", "信用卡", "クレジット", "신용카드"]),

        new(PiiLocale.Global, PiiEntityType.Iban, "IBAN",
            R(@"(?<![A-Z])[A-Z]{2}\d{2}[\s]?[\dA-Z]{4}[\s]?(?:[\dA-Z]{4}[\s]?){1,7}[\dA-Z]{1,4}(?![A-Z])"),
            0.70, Mod97Check, null, ValidateWithOriginalText: true),

        new(PiiLocale.Global, PiiEntityType.IpAddress, "IPv4 Address",
            R(@"(?<!\d)(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)(?!\d)"),
            0.60, null, ["ip", "address", "server", "host", "位址", "伺服器"]),

        new(PiiLocale.Global, PiiEntityType.Url, "URL",
            R(@"https?://[^\s<>""']+"),
            0.70, null, null),

        new(PiiLocale.Global, PiiEntityType.CryptoWallet, "Bitcoin Address",
            R(@"(?<!\w)(?:1|3|bc1)[a-zA-HJ-NP-Z0-9]{25,62}(?!\w)"),
            0.80, null, ["bitcoin", "btc", "wallet", "比特幣"]),

        new(PiiLocale.Global, PiiEntityType.CryptoWallet, "Ethereum Address",
            R(@"(?<!\w)0x[0-9a-fA-F]{40}(?!\w)"),
            0.85, null, ["ethereum", "eth", "wallet", "以太幣"]),

        new(PiiLocale.Global, PiiEntityType.MacAddress, "MAC Address",
            R(@"(?<!\w)(?:[0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}(?!\w)"),
            0.70, null, ["mac", "network", "interface", "網路"]),

        new(PiiLocale.Global, PiiEntityType.DateOfBirth, "Date of Birth",
            R(@"(?<!\d)(?:19|20)\d{2}[-/.](?:0[1-9]|1[0-2])[-/.](?:0[1-9]|[12]\d|3[01])(?!\d)"),
            0.50, null, ["birthday", "born", "birth", "dob", "生日", "出生", "誕生日", "생일"]),

        // ─── TW（台灣）───
        new(PiiLocale.TW, PiiEntityType.IdCard, "台灣身分證",
            R(@"(?<![A-Z])[A-Z][12]\d{8}(?!\d)"),
            0.90, TwIdCheck, ["身分證", "身份證", "id card", "ID"], ValidateWithOriginalText: true),

        new(PiiLocale.TW, PiiEntityType.Phone, "台灣電話",
            R(@"(?<!\d)0\d{1,2}-?\d{3,4}-?\d{3,4}(?!\d)"),
            0.80, null, ["電話", "手機", "聯絡", "tel", "phone"]),

        new(PiiLocale.TW, PiiEntityType.TaxId, "統一編號",
            R(@"(?<!\d)\d{8}(?!\d)"),
            0.55, TwBusinessIdCheck, ["統一編號", "統編", "公司", "營業"]),

        new(PiiLocale.TW, PiiEntityType.MedicalId, "健保卡",
            R(@"(?<!\d)\d{12}(?!\d)"),
            0.50, null, ["健保", "健保卡", "NHI", "就醫"]),

        new(PiiLocale.TW, PiiEntityType.PostalCode, "台灣郵遞區號",
            R(@"(?<!\d)[1-9]\d{2}(?:\d{2})?(?!\d)"),
            0.30, null, ["郵遞區號", "郵編", "postal", "zip"]),

        new(PiiLocale.TW, PiiEntityType.Address, "台灣地址",
            R(@"[\u4e00-\u9fff]{2,6}[市縣][\u4e00-\u9fff]{1,6}[區鄉鎮市][\u4e00-\u9fff\d\-]+[路街道巷弄號樓層F]+[\d\-]*[\u4e00-\u9fff\d]*"),
            0.85, null, ["地址", "住址", "address"]),

        // ─── JP（日本）───
        new(PiiLocale.JP, PiiEntityType.IdCard, "My Number",
            R(@"(?<!\d)\d{12}(?!\d)"),
            0.60, JpMyNumberCheck, ["マイナンバー", "my number", "個人番号"]),

        new(PiiLocale.JP, PiiEntityType.Phone, "日本電話",
            R(@"(?<!\d)0\d{1,4}-\d{1,4}-\d{4}(?!\d)"),
            0.80, null, ["電話", "携帯", "tel", "phone"]),

        new(PiiLocale.JP, PiiEntityType.Passport, "日本護照",
            R(@"(?<![A-Z])[A-Z]{2}\d{7}(?!\d)"),
            0.65, null, ["パスポート", "passport", "護照", "旅券"]),

        new(PiiLocale.JP, PiiEntityType.DriverLicense, "日本駕照",
            R(@"(?<!\d)\d{12}(?!\d)"),
            0.35, null, ["免許", "運転免許", "driver", "license", "駕照"]),

        new(PiiLocale.JP, PiiEntityType.PostalCode, "郵便番號",
            R(@"(?<!\d)\d{3}-\d{4}(?!\d)"),
            0.60, null, ["郵便番号", "〒", "postal", "zip"]),

        new(PiiLocale.JP, PiiEntityType.TaxId, "法人番號",
            R(@"(?<!\d)\d{13}(?!\d)"),
            0.55, null, ["法人番号", "法人", "corporate number"]),

        // ─── KR（韓國）───
        new(PiiLocale.KR, PiiEntityType.IdCard, "住民登錄番號",
            R(@"(?<!\d)\d{6}-\d{7}(?!\d)"),
            0.90, s => KrRrnCheck(s), ["주민등록", "resident", "RRN"]),

        new(PiiLocale.KR, PiiEntityType.Phone, "韓國電話",
            R(@"(?<!\d)01[016789]-\d{3,4}-\d{4}(?!\d)"),
            0.80, null, ["전화", "핸드폰", "tel", "phone"]),

        new(PiiLocale.KR, PiiEntityType.Passport, "韓國護照",
            R(@"(?<![A-Z])[A-Z]{1,2}\d{7,8}(?!\d)"),
            0.60, null, ["여권", "passport", "護照"]),

        new(PiiLocale.KR, PiiEntityType.DriverLicense, "韓國駕照",
            R(@"(?<!\d)\d{2}-\d{6}-\d{2}(?!\d)"),
            0.70, null, ["운전면허", "driver", "license", "駕照"]),

        new(PiiLocale.KR, PiiEntityType.TaxId, "事業者登錄番號",
            R(@"(?<!\d)\d{3}-\d{2}-\d{5}(?!\d)"),
            0.75, null, ["사업자등록", "사업자", "business registration"]),

        // ─── US（美國）───
        new(PiiLocale.US, PiiEntityType.Ssn, "US SSN",
            R(@"(?<!\w)\d{3}-\d{2}-\d{4}(?!\w)"),
            0.90, null, ["ssn", "social security", "社會安全碼"]),

        new(PiiLocale.US, PiiEntityType.Phone, "US Phone",
            R(@"(?<!\d)(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}(?!\d)"),
            0.75, null, ["phone", "call", "tel", "mobile", "cell"]),

        new(PiiLocale.US, PiiEntityType.Passport, "US Passport",
            R(@"(?<!\d)\d{9}(?!\d)"),
            0.40, null, ["passport", "護照"]),

        new(PiiLocale.US, PiiEntityType.TaxId, "US TIN/EIN",
            R(@"(?<!\d)\d{2}-\d{7}(?!\d)"),
            0.70, null, ["tin", "ein", "tax", "employer", "稅號"]),

        // ─── UK（英國）───
        new(PiiLocale.UK, PiiEntityType.MedicalId, "UK NHS Number",
            R(@"(?<!\d)\d{3}[\s]?\d{3}[\s]?\d{4}(?!\d)"),
            0.60, UkNhsCheck, ["nhs", "national health", "NHS"]),

        new(PiiLocale.UK, PiiEntityType.IdCard, "UK NINO",
            R(@"(?<![A-Z])[A-CEGHJ-PR-TW-Z]{2}\d{6}[A-D](?![A-Z])"),
            0.85, null, ["nino", "national insurance", "NI number"]),

        new(PiiLocale.UK, PiiEntityType.Passport, "UK Passport",
            R(@"(?<!\d)\d{9}(?!\d)"),
            0.40, null, ["passport", "護照"]),

        new(PiiLocale.UK, PiiEntityType.PostalCode, "UK Postcode",
            R(@"(?<!\w)[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}(?!\w)"),
            0.70, null, ["postcode", "postal", "address", "郵遞區號"]),
    ];
}
