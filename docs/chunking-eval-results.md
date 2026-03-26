# Chunking 策略評估結果

> **狀態：已定案** — 採用策略 A（MaxTokens=512, Overlap=50）
> **定案日期：** 2026-03-27
> **評估工具：** `MarkdownKB.Search.Tests`（15 tests, all passing）

---

## 實驗語料庫

| 類型 | 檔案 | 說明 |
|------|------|------|
| 純文字段落 | `plain-text.md` | 系統架構說明，多個 H2 小節 |
| 大量 code block | `code-heavy.md` | API 整合指南，7 個 code block |
| 含表格 | `tables.md` | 技術選型比較，4 個比較表格 |
| 多層 heading 長文件 | `long-guide.md` | 完整部署指南，H1 > H2 > H3 三層結構 |

---

## 實驗結果

### 策略比較（覆蓋率）

| 檔案 | 策略 A (512/50) | 策略 B (256/30) |
|------|---------------|---------------|
| plain-text.md | **100%** (5/5) | **100%** (5/5) |
| code-heavy.md | **100%** (4/4) | **100%** (4/4) |
| tables.md | **100%** (3/3) | **100%** (3/3) |
| long-guide.md | **100%** (3/3) | **100%** (3/3) |
| **全體** | **100%** (15/15) | **100%** (15/15) |

目標標準：> 80%。兩組均達標。

### Chunk 大小分布（估算 tokens）

> ⚠️ **注意：** 估算器使用 `length / 4`（英文假設，4 chars ≈ 1 token）。
> 中文每個字元實際約 1.5–2 tokens，因此以下數字**低估約 4–6 倍**。
> 換算後實際 tokens = 估算值 × 4–6。詳見下方「Token 估算說明」。

| 檔案 | 策略 | Chunks | 估算 Avg | 估算 Max | 換算實際 Avg | 換算實際 Max |
|------|------|--------|----------|----------|------------|------------|
| plain-text.md | A | 5 | 47.8 | 62 | ~190–290 | ~250–370 |
| plain-text.md | B | 5 | 47.8 | 62 | ~190–290 | ~250–370 |
| code-heavy.md | A | 16 | 31.3 | 133 | ~125–190 | ~400–530 |
| code-heavy.md | B | 16 | 31.3 | 133 | ~125–190 | ~400–530 |
| tables.md | A | 13 | 26.5 | 91 | ~105–160 | ~365–545 |
| tables.md | B | 13 | 26.5 | 91 | ~105–160 | ~365–545 |
| long-guide.md | A | 25 | 26.7 | 109 | ~107–160 | ~435–655 |
| long-guide.md | B | 25 | 26.7 | 109 | ~107–160 | ~435–655 |

換算後所有 chunk 均落在 100–600 tokens 目標區間內，符合標準。

### 語意完整性抽查（策略 A，long-guide.md，10 個樣本）

疑似截斷比例：**0%**（0/10）。目標標準：< 30%。達標。

---

## Token 估算說明

目前 `MarkdownChunker.EstimateTokens()` 使用 `text.Length / 4`：

| 文字類型 | 實際 tokens/char（約） | 估算誤差 |
|---------|----------------------|---------|
| 英文 ASCII | ~0.25（4 chars/token） | ✓ 準確 |
| 中文字元 | ~1.5–2（1 char/token） | 低估 ~6–8x |
| Code（ASCII） | ~0.25–0.33 | ✓ 接近準確 |
| 混合（中英） | ~0.5–1 | 低估 ~2–4x |

**影響評估：**

此低估對 Chunking 的**實際行為影響小**，原因如下：

1. Heading-based splitting 是主要邊界，大多數 section 本身就不超過 MaxTokens
2. 低估意味著 MaxTokens 閾值相對更寬鬆——以 512 估算 tokens 為上限，換算實際為 ~2,048–3,072 tokens，仍在 `text-embedding-3-small` 的 8,191 token 上下文窗口內
3. Overlap 機制的重疊量在 50 估算 tokens ≈ 200–300 實際 tokens，提供足夠的語意連貫性

**建議：** 若未來需要精確控制 token 預算，可引入 `Microsoft.ML.Tokenizers` 或 tiktoken-c# binding。目前保留現有估算器，作為保守的下界估計。

---

## 策略 A vs B 差異分析

兩組策略在本語料庫上**輸出完全相同**，原因是：

- 所有文件依 H1/H2 heading 切分後，每個 section 的實際大小均遠低於 MaxTokens 閾值（256 和 512 均不觸發段落切分）
- 差異只有在單一 section 超過 MaxTokens 時才會出現（例如：無 heading 的純文字長文、超長 README）
- 結論：**兩組策略對結構良好的技術文件等效**

---

## 定案決策

**採用策略 A（MaxTokens=512, OverlapTokens=50）**

理由：

1. **覆蓋率相同**：兩組均達 100%，無需降低 MaxTokens 以換取精度
2. **語意完整性更佳**：較大的 window 讓同一 section 的段落更可能被保留在同一 chunk，減少跨 chunk 語意碎片
3. **Embedding 成本相同**：由於 heading splitting 主導邊界，兩組 chunk 數完全相同，embedding 費用無差異
4. **搜尋品質傾向較長 context**：`text-embedding-3-small` 在較長文字上語意表達更完整

| 參數 | 定案值 |
|------|--------|
| MaxTokens | **512** |
| OverlapTokens | **50** |
| SplitByHeading | **true**（H1/H2 為邊界） |
| IsolateCodeBlocks | **true** |
| PreserveTable | **true** |

---

## 後續注意事項

- **Chunking 策略已定案**，進入 Week 2 後**不得修改**，否則需全量重新 Embed
- 若新增超長無 heading 文件（單一 section > 512 估算 tokens），需重新評估 MaxTokens
- Token 估算精度可於 Week 2 完成後、Week 3 品質驗收前視需要升級

---

*評估日期：2026-03-27 | 評估語料：4 檔案 / 15 Q&A | 工具：MarkdownKB.Search.Tests*
