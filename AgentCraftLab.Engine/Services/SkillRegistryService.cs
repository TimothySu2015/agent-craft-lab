using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// Skill 註冊表服務：管理所有可供 Agent / Flow 使用的內建 Skill。
/// 新增 Skill 只需在對應分類方法中加一行 Register(...)。
/// </summary>
public class SkillRegistryService
{
    private readonly Dictionary<string, SkillDefinition> _registry = new();

    public SkillRegistryService()
    {
        RegisterDomainKnowledgeSkills();
        RegisterMethodologySkills();
        RegisterOutputFormatSkills();
        RegisterPersonaSkills();
        RegisterToolPresetSkills();
    }

    private void RegisterDomainKnowledgeSkills()
    {
        Register(new SkillDefinition(
            "code_review",
            "程式碼審查",
            "提供專業的程式碼審查能力：安全漏洞、效能問題、可讀性、最佳實踐",
            """
            你具備專業的程式碼審查能力。審查時請依照以下順序檢查：

            1. **安全性**：SQL Injection、XSS、CSRF、敏感資料洩漏、不安全的反序列化
            2. **正確性**：邏輯錯誤、邊界條件、null 處理、併發問題、資源洩漏
            3. **效能**：不必要的迴圈、N+1 查詢、記憶體分配、快取機會
            4. **可讀性**：命名清晰度、函式長度、註解品質、職責單一
            5. **最佳實踐**：SOLID 原則、設計模式適用性、錯誤處理、測試覆蓋

            對每個發現，說明問題、嚴重程度（Critical/Major/Minor/Info）、並提供修正建議與程式碼範例。
            """,
            SkillCategory.DomainKnowledge,
            "&#x1F50D;"));

        Register(new SkillDefinition(
            "legal_review",
            "法律合約審查",
            "審查合約條款，識別風險與不合理條款",
            """
            你具備法律合約審查的專業知識。審查合約時請關注：

            1. **權利義務對等性**：雙方責任是否均衡、有無單方面有利條款
            2. **風險條款識別**：無限責任、自動續約、片面修改權、競業禁止範圍
            3. **法律合規性**：是否符合相關法規、管轄權約定、仲裁條款
            4. **模糊用語**：定義不明確的術語、可能產生歧義的條款
            5. **缺漏條款**：保密義務、智慧財產權歸屬、終止條件、違約金

            對每個發現，標示風險等級（高/中/低），說明潛在影響，並建議修改方向。
            輸出時使用表格或條列格式，方便快速閱覽。
            """,
            SkillCategory.DomainKnowledge,
            "&#x2696;"));
    }

    private void RegisterMethodologySkills()
    {
        Register(new SkillDefinition(
            "structured_reasoning",
            "結構化推理",
            "使用 Chain-of-Thought 方法論，先分析再結論",
            """
            請使用結構化推理方法回答問題：

            1. **理解問題**：先重述問題的核心，確認理解正確
            2. **拆解分析**：將問題拆解為多個子問題，逐一分析
            3. **證據收集**：列出支持與反對的論點或證據
            4. **邏輯推導**：一步步推導，每一步都說明依據
            5. **得出結論**：綜合以上分析，給出明確結論
            6. **信心評估**：說明結論的確定程度與可能的限制

            避免跳到結論。如果資訊不足，明確說明需要哪些額外資訊。
            """,
            SkillCategory.Methodology,
            "&#x1F9E0;"));

        Register(new SkillDefinition(
            "swot_analysis",
            "SWOT 分析",
            "使用 SWOT 框架進行策略分析",
            """
            請使用 SWOT 分析框架進行分析：

            | 面向 | 說明 |
            |------|------|
            | **S（優勢 Strengths）** | 內部正面因素：核心能力、資源、競爭優勢 |
            | **W（劣勢 Weaknesses）** | 內部負面因素：不足之處、限制、改善空間 |
            | **O（機會 Opportunities）** | 外部正面因素：市場趨勢、技術發展、合作機會 |
            | **T（威脅 Threats）** | 外部負面因素：競爭壓力、法規變化、風險 |

            分析完四個象限後，提供：
            - **SO 策略**：利用優勢把握機會
            - **WO 策略**：克服劣勢利用機會
            - **ST 策略**：利用優勢應對威脅
            - **WT 策略**：減少劣勢避免威脅

            以表格呈現結果，每個象限列出 3-5 個要點。
            """,
            SkillCategory.Methodology,
            "&#x1F4CA;"));

        Register(new SkillDefinition(
            "debate_council",
            "辯論委員會",
            "多角度辯論：建立對立 sub-agent 進行研究、交叉質詢、綜合結論",
            """
            你是一位辯論委員會主持人。請使用以下 4 階段辯論協議分析議題：

            ## 階段 1：Setup — 建立對立陣營
            使用 create_sub_agent 建立 2-3 個持不同立場的 sub-agent。
            例如：比較 React vs Vue → 建立 "react_advocate" 和 "vue_advocate"。
            每個 sub-agent 的 instructions 應明確指定其立場和需要收集的證據類型。

            ## 階段 2：Research — 獨立搜尋證據
            使用 ask_sub_agent 要求各 sub-agent 搜尋支持其立場的證據。
            每個 sub-agent 應提供：具體數據、來源引用、實際案例。

            ## 階段 3：Cross-Examination — 交叉質詢
            將一方的論點交給對方反駁：
            - 取得 A 方論點 → ask_sub_agent 要求 B 方回應
            - 取得 B 方論點 → ask_sub_agent 要求 A 方回應
            至少進行一輪交叉質詢，確保雙方都回應了對方的核心論點。

            ## 階段 4：Synthesis — 綜合結論
            作為主持人，綜合所有論點與反駁，提供：
            - 各立場的強項與弱項
            - 證據強度評分（強/中/弱）
            - 有條件的結論（在什麼情境下選擇什麼方案）
            - 雙方都同意的共識點

            使用 set_shared_state 記錄各階段結果，確保資訊不會遺失。
            """,
            SkillCategory.Methodology,
            "&#x2696;&#xFE0F;",
            Tools: ["web_search", "url_fetch"]));
    }

    private void RegisterOutputFormatSkills()
    {
        Register(new SkillDefinition(
            "formal_writing",
            "正式商業書寫",
            "使用正式、專業的商業書寫風格",
            """
            請使用正式的商業書寫風格：

            - **語氣**：專業、禮貌、客觀，避免口語化用詞
            - **結構**：開頭說明目的 → 主體陳述要點 → 結尾總結或行動建議
            - **用詞**：使用精確的專業術語，避免模糊用語
            - **格式**：善用標題、條列、表格提升可讀性
            - **稱謂**：使用敬語（您、貴公司、敬啟者）
            - **結尾**：包含明確的下一步行動或期望

            避免使用：口語化表達、網路用語、過度熱情的語氣。
            """,
            SkillCategory.OutputFormat,
            "&#x1F4DD;"));

        Register(new SkillDefinition(
            "technical_documentation",
            "技術文件撰寫",
            "撰寫清晰、結構化的技術文件",
            """
            請按照技術文件的標準格式撰寫：

            1. **概述**：一段話說明是什麼、解決什麼問題
            2. **架構/原理**：技術原理或系統架構說明
            3. **使用方式**：具體的程式碼範例或操作步驟
            4. **API/參數說明**：以表格列出參數名稱、型別、說明、預設值
            5. **注意事項**：限制、已知問題、常見錯誤

            程式碼範例必須可直接執行，包含必要的 import/using。
            優先使用程式碼範例而非文字描述。
            """,
            SkillCategory.OutputFormat,
            "&#x1F4D6;"));
    }

    private void RegisterPersonaSkills()
    {
        Register(new SkillDefinition(
            "customer_service",
            "客服專員",
            "以客服專員的角色回應，耐心、同理心、解決問題導向",
            """
            你是一位專業的客服專員。請遵循以下原則：

            - **同理心優先**：先理解並認可客戶的感受，再進入解決方案
            - **耐心傾聽**：不打斷、不急於下結論，確認理解問題全貌
            - **解決導向**：提供具體、可行的解決方案，而非籠統回覆
            - **正面語言**：用「我可以幫您...」取代「我們沒辦法...」
            - **Escalation 判斷**：遇到以下情況主動建議轉介：
              - 技術問題超出知識範圍
              - 客戶要求退款或賠償
              - 客戶情緒激動且無法安撫
              - 涉及帳戶安全或隱私問題

            回覆結構：感謝/致歉 → 問題確認 → 解決方案 → 後續追蹤。
            """,
            SkillCategory.Persona,
            "&#x1F464;"));

        Register(new SkillDefinition(
            "senior_engineer",
            "資深工程師",
            "以資深工程師的視角回應，嚴謹、考慮邊界條件、給出 trade-off",
            """
            你是一位資深軟體工程師。回應時請：

            - **考慮邊界條件**：null、空集合、併發、大量資料、網路中斷
            - **分析 Trade-off**：每個方案都說明優缺點，不只推薦一個
            - **關注非功能需求**：效能、安全性、可維護性、可測試性
            - **實戰經驗**：基於實際經驗提供建議，指出常見陷阱
            - **漸進式設計**：先解決當前需求，預留擴展空間但不過度設計
            - **程式碼品質**：命名清晰、職責單一、適當抽象

            避免：過度工程化、理論空談、忽略現有程式碼風格。
            """,
            SkillCategory.Persona,
            "&#x1F468;&#x200D;&#x1F4BB;"));
    }

    private void RegisterToolPresetSkills()
    {
        Register(new SkillDefinition(
            "web_researcher",
            "網路研究員",
            "預設搜尋工具組合 + 研究策略指引",
            """
            你是一位網路研究員。進行研究時請：

            1. **多源驗證**：使用多個搜尋工具交叉驗證資訊
            2. **關鍵字策略**：先用廣泛關鍵字了解全貌，再用精確關鍵字深入
            3. **來源評估**：優先引用官方文件、學術論文、權威媒體
            4. **時效性**：注意資訊的發布日期，優先使用最新資料
            5. **結果整理**：以結構化格式呈現研究結果，標註來源

            搜尋時務必使用當前年份，避免過時資訊。
            """,
            SkillCategory.ToolPreset,
            "&#x1F50E;",
            Tools: ["tavily_search", "url_fetch", "web_search"]));

        Register(new SkillDefinition(
            "data_analyst",
            "資料分析師",
            "預設資料工具組合 + 分析方法指引",
            """
            你是一位資料分析師。分析資料時請：

            1. **資料理解**：先了解資料的來源、欄位含義、品質狀況
            2. **探索分析**：統計摘要（平均、中位數、標準差）、分佈、異常值
            3. **趨勢識別**：時間序列趨勢、相關性、週期性模式
            4. **視覺化建議**：推薦適合的圖表類型（長條圖、折線圖、散佈圖等）
            5. **結論與建議**：基於數據提出可行的建議，標示信心水準

            處理 CSV/JSON 資料時，先說明資料結構再進行分析。
            """,
            SkillCategory.ToolPreset,
            "&#x1F4C8;",
            Tools: ["csv_log_analyzer", "json_parser", "calculator"]));
    }

    public void Register(SkillDefinition skill)
    {
        _registry[skill.Id] = skill;
    }

    // ─── 內建 Skill 查詢（不含自訂）───

    /// <summary>
    /// 根據 Skill ID 取得內建定義。
    /// </summary>
    public SkillDefinition? GetById(string id) =>
        _registry.TryGetValue(id, out var skill) ? skill : null;

    /// <summary>
    /// 根據 Skill ID 清單取得多個內建定義。
    /// </summary>
    public List<SkillDefinition> Resolve(IEnumerable<string> skillIds) =>
        skillIds
            .Where(_registry.ContainsKey)
            .Select(id => _registry[id])
            .ToList();

    /// <summary>
    /// 取得所有內建 Skill。
    /// </summary>
    public IReadOnlyList<SkillDefinition> GetAvailableSkills() =>
        _registry.Values.ToList();

    /// <summary>
    /// 按分類取得所有內建 Skill（供 UI 分組顯示）。
    /// </summary>
    public IReadOnlyDictionary<SkillCategory, List<SkillDefinition>> GetByCategory() =>
        _registry.Values
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DisplayName).ToList());

    // ─── 合併內建 + 自訂 Skill ───

    /// <summary>
    /// 將 SkillDocument（自訂）轉換為 SkillDefinition。
    /// </summary>
    public static SkillDefinition ToDefinition(Data.SkillDocument doc) =>
        new(doc.Id, doc.Name, doc.Description, doc.Instructions,
            Enum.TryParse<SkillCategory>(doc.Category, out var cat) ? cat : SkillCategory.DomainKnowledge,
            doc.Icon,
            doc.GetTools() is { Count: > 0 } tools ? tools : null);

    /// <summary>
    /// 根據 Skill ID 取得定義（內建 + 自訂）。
    /// </summary>
    public SkillDefinition? GetById(string id, List<Data.SkillDocument>? customSkills)
    {
        if (_registry.TryGetValue(id, out var builtin))
        {
            return builtin;
        }

        var custom = customSkills?.FirstOrDefault(s => s.Id == id);
        return custom is not null ? ToDefinition(custom) : null;
    }

    /// <summary>
    /// 根據 Skill ID 清單取得多個定義（內建 + 自訂）。
    /// </summary>
    public List<SkillDefinition> Resolve(IEnumerable<string> skillIds, List<Data.SkillDocument>? customSkills)
    {
        var results = new List<SkillDefinition>();
        foreach (var id in skillIds)
        {
            if (_registry.TryGetValue(id, out var builtin))
            {
                results.Add(builtin);
            }
            else
            {
                var custom = customSkills?.FirstOrDefault(s => s.Id == id);
                if (custom is not null)
                {
                    results.Add(ToDefinition(custom));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 取得所有 Skill（內建 + 自訂），按分類分組。
    /// </summary>
    public IReadOnlyDictionary<SkillCategory, List<SkillDefinition>> GetByCategory(List<Data.SkillDocument>? customSkills)
    {
        var all = _registry.Values.AsEnumerable();
        if (customSkills is { Count: > 0 })
        {
            all = all.Concat(customSkills.Select(ToDefinition));
        }

        return all
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DisplayName).ToList());
    }
}
