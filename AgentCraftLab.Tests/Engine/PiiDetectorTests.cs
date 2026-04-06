using AgentCraftLab.Engine.Pii;

namespace AgentCraftLab.Tests.Engine;

public class PiiDetectorTests
{
    private static RegexPiiDetector AllLocales() => new();
    private static RegexPiiDetector OnlyLocale(PiiLocale locale) => new([locale]);

    // ─── Global ───

    [Fact]
    public void Detect_Email()
    {
        var results = AllLocales().Detect("Contact me at john@example.com please");
        Assert.Single(results);
        Assert.Equal(PiiEntityType.Email, results[0].Type);
        Assert.Equal("john@example.com", results[0].Text);
    }

    [Fact]
    public void Detect_InternationalPhone()
    {
        var results = AllLocales().Detect("Call me at +886-2-1234-5678");
        Assert.Contains(results, e => e.Type == PiiEntityType.InternationalPhone);
    }

    [Fact]
    public void Detect_CreditCard_LuhnPass()
    {
        // Valid Luhn: 4111 1111 1111 1111
        var results = AllLocales().Detect("Card: 4111 1111 1111 1111");
        Assert.Contains(results, e => e.Type == PiiEntityType.CreditCard && e.Confidence > 0.9);
    }

    [Fact]
    public void Detect_CreditCard_LuhnFail()
    {
        // Invalid Luhn: 4111 1111 1111 1112
        var results = AllLocales().Detect("Card: 4111 1111 1111 1112", 0.3);
        var cc = results.FirstOrDefault(e => e.Type == PiiEntityType.CreditCard);
        // Should still detect but with lower confidence
        Assert.True(cc is null || cc.Confidence < 0.7);
    }

    [Fact]
    public void Detect_IPv4()
    {
        var results = AllLocales().Detect("Server IP: 192.168.1.100");
        Assert.Contains(results, e => e.Type == PiiEntityType.IpAddress);
    }

    [Fact]
    public void Detect_Url()
    {
        var results = AllLocales().Detect("Visit https://example.com/path?q=1");
        Assert.Contains(results, e => e.Type == PiiEntityType.Url);
    }

    [Fact]
    public void Detect_MacAddress()
    {
        var results = AllLocales().Detect("MAC: 00:1A:2B:3C:4D:5E");
        Assert.Contains(results, e => e.Type == PiiEntityType.MacAddress);
    }

    [Fact]
    public void Detect_EthereumAddress()
    {
        var results = AllLocales().Detect("ETH: 0x742d35Cc6634C0532925a3b844Bc9e7595f2bD38");
        Assert.Contains(results, e => e.Type == PiiEntityType.CryptoWallet);
    }

    [Fact]
    public void Detect_DateOfBirth_WithContext()
    {
        var results = AllLocales().Detect("生日 1990-05-15 是他的");
        Assert.Contains(results, e => e.Type == PiiEntityType.DateOfBirth);
    }

    // ─── TW ───

    [Fact]
    public void Detect_TwIdCard_Valid()
    {
        // A123456789 is a valid TW ID
        var results = OnlyLocale(PiiLocale.TW).Detect("身分證字號 A123456789");
        Assert.Contains(results, e => e.Type == PiiEntityType.IdCard && e.Label == "台灣身分證");
    }

    [Fact]
    public void Detect_TwIdCard_InvalidChecksum()
    {
        var results = OnlyLocale(PiiLocale.TW).Detect("A123456780");
        var id = results.FirstOrDefault(e => e.Type == PiiEntityType.IdCard && e.Label == "台灣身分證");
        // Should detect but with lower confidence due to failed checksum
        Assert.True(id is null || id.Confidence < 0.8);
    }

    [Fact]
    public void Detect_TwPhone()
    {
        var results = OnlyLocale(PiiLocale.TW).Detect("電話 02-1234-5678");
        Assert.Contains(results, e => e.Type == PiiEntityType.Phone && e.Label == "台灣電話");
    }

    [Fact]
    public void Detect_TwAddress()
    {
        var results = OnlyLocale(PiiLocale.TW).Detect("台北市大安區忠孝東路四段100號5樓");
        Assert.Contains(results, e => e.Type == PiiEntityType.Address);
    }

    // ─── JP ───

    [Fact]
    public void Detect_JpPhone()
    {
        var results = OnlyLocale(PiiLocale.JP).Detect("電話 03-1234-5678");
        Assert.Contains(results, e => e.Type == PiiEntityType.Phone && e.Label == "日本電話");
    }

    [Fact]
    public void Detect_JpPostalCode()
    {
        var results = OnlyLocale(PiiLocale.JP).Detect("〒100-0001");
        Assert.Contains(results, e => e.Type == PiiEntityType.PostalCode && e.Label == "郵便番號");
    }

    // ─── KR ───

    [Fact]
    public void Detect_KrPhone()
    {
        var results = OnlyLocale(PiiLocale.KR).Detect("전화 010-1234-5678");
        Assert.Contains(results, e => e.Type == PiiEntityType.Phone && e.Label == "韓國電話");
    }

    [Fact]
    public void Detect_KrBusinessNumber()
    {
        var results = OnlyLocale(PiiLocale.KR).Detect("사업자 123-45-67890");
        Assert.Contains(results, e => e.Type == PiiEntityType.TaxId);
    }

    // ─── US ───

    [Fact]
    public void Detect_UsSsn()
    {
        var results = OnlyLocale(PiiLocale.US).Detect("SSN: 123-45-6789");
        Assert.Contains(results, e => e.Type == PiiEntityType.Ssn);
    }

    // ─── UK ───

    [Fact]
    public void Detect_UkNino()
    {
        var results = OnlyLocale(PiiLocale.UK).Detect("NINO: AB123456C");
        Assert.Contains(results, e => e.Type == PiiEntityType.IdCard && e.Label == "UK NINO");
    }

    [Fact]
    public void Detect_UkPostcode()
    {
        var results = OnlyLocale(PiiLocale.UK).Detect("Postcode: SW1A 1AA");
        Assert.Contains(results, e => e.Type == PiiEntityType.PostalCode);
    }

    // ─── Locale 篩選 ───

    [Fact]
    public void LocaleFilter_OnlyTW_DoesNotDetectUsSsn()
    {
        var results = OnlyLocale(PiiLocale.TW).Detect("SSN: 123-45-6789");
        Assert.DoesNotContain(results, e => e.Type == PiiEntityType.Ssn);
    }

    [Fact]
    public void LocaleFilter_OnlyGlobal_DoesNotDetectTwId()
    {
        var results = OnlyLocale(PiiLocale.Global).Detect("A123456789");
        Assert.DoesNotContain(results, e => e.Label == "台灣身分證");
    }

    // ─── Context-aware ───

    [Fact]
    public void ContextKeywords_BoostConfidence()
    {
        var detector = OnlyLocale(PiiLocale.Global);
        var withContext = detector.Detect("Server IP address: 10.0.0.1", 0.01);
        var withoutContext = detector.Detect("Version 10.0.0.1", 0.01);

        var ipWith = withContext.FirstOrDefault(e => e.Type == PiiEntityType.IpAddress);
        var ipWithout = withoutContext.FirstOrDefault(e => e.Type == PiiEntityType.IpAddress);

        Assert.NotNull(ipWith);
        Assert.NotNull(ipWithout);
        Assert.True(ipWith.Confidence > ipWithout.Confidence);
    }

    // ─── Overlap ───

    [Fact]
    public void OverlapResolution_KeepsHigherConfidence()
    {
        // Multiple locales might match same pattern, highest confidence should win
        var detector = AllLocales();
        var results = detector.Detect("Call me at john@example.com");
        // Email should appear once, not duplicated
        Assert.Single(results, e => e.Type == PiiEntityType.Email);
    }

    // ─── Threshold ───

    [Fact]
    public void Threshold_FiltersLowConfidence()
    {
        var detector = AllLocales();
        var highThreshold = detector.Detect("10.0.0.1", 0.99);
        Assert.Empty(highThreshold);
    }

    // ─── Legacy config ───

    [Fact]
    public void FromConfig_ParsesLocales()
    {
        var config = new Dictionary<string, string> { ["locales"] = "global,tw" };
        var detector = RegexPiiDetector.FromConfig(config);
        var results = detector.Detect("Email: test@test.com, 身分證 A123456789");
        Assert.Contains(results, e => e.Type == PiiEntityType.Email);
    }

    [Fact]
    public void FromConfig_CustomRules()
    {
        var config = new Dictionary<string, string> { ["customRules"] = "OrderId:ORD-\\d{6}" };
        var detector = RegexPiiDetector.FromConfig(config);
        var results = detector.Detect("Order: ORD-123456");
        Assert.Contains(results, e => e.Type == PiiEntityType.Custom && e.Label == "OrderId");
    }

    // ─── IBAN ───

    [Fact]
    public void Detect_Iban_ValidMod97()
    {
        // GB29 NWBK 6016 1331 9268 19 is a valid UK IBAN
        var results = AllLocales().Detect("IBAN: GB29NWBK60161331926819");
        Assert.Contains(results, e => e.Type == PiiEntityType.Iban && e.Confidence > 0.9);
    }

    [Fact]
    public void Detect_Iban_InvalidMod97()
    {
        // Invalid: change last digit
        var results = AllLocales().Detect("IBAN: GB29NWBK60161331926810", 0.1);
        var iban = results.FirstOrDefault(e => e.Type == PiiEntityType.Iban);
        Assert.True(iban is null || iban.Confidence < 0.7);
    }

    // ─── PiiEntity Locale 欄位 ───

    [Fact]
    public void DetectedEntity_HasCorrectLocale()
    {
        var results = OnlyLocale(PiiLocale.TW).Detect("電話 02-1234-5678");
        var phone = results.FirstOrDefault(e => e.Type == PiiEntityType.Phone);
        Assert.NotNull(phone);
        Assert.Equal(PiiLocale.TW, phone.Locale);
    }

    [Fact]
    public void GlobalEntity_HasGlobalLocale()
    {
        var results = OnlyLocale(PiiLocale.Global).Detect("test@example.com");
        var email = results.FirstOrDefault(e => e.Type == PiiEntityType.Email);
        Assert.NotNull(email);
        Assert.Equal(PiiLocale.Global, email.Locale);
    }

    // ─── 邊界 ───

    [Fact]
    public void Detect_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(AllLocales().Detect(""));
    }

    [Fact]
    public void Detect_NoPii_ReturnsEmpty()
    {
        Assert.Empty(AllLocales().Detect("Hello, this is a normal message."));
    }

    [Fact]
    public void FromConfig_NullConfig_ReturnsDefaultDetector()
    {
        var detector = RegexPiiDetector.FromConfig(null);
        var results = detector.Detect("test@example.com");
        Assert.NotEmpty(results);
    }
}
