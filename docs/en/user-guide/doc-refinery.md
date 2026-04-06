# DocRefinery — Document Refinement

DocRefinery is AgentCraftLab's document cleaning and structured output feature. It takes multiple source files in different formats (PDF, DOCX, PPTX, XLSX, HTML, TXT, images) and automatically cleans and consolidates them into a standardized structured specification document.

---

## 1. Core Concepts

```
Multiple source files → Clean (remove noise) → Structured extraction (LLM) → Specification document
```

| Concept | Description |
|---------|-------------|
| **Refinery Project** | A workspace containing multiple source files and versioned outputs |
| **Cleaning** | Parses raw documents into typed elements (Title, NarrativeText, Table, ListItem...), removes headers/footers, normalizes whitespace |
| **Schema Template** | Defines the output document structure (e.g., Software Requirements Spec has 13 sections) |
| **Fast Mode** | Single LLM call, good for small files |
| **Precise Mode** | Multi-layer Agent + Search, better for large/multiple files |

---

## 2. Workflow

### Step 1: Create a Project

1. Click 🏭 **Doc Refinery** in the sidebar
2. Click **Create** in the top right
3. Enter project name and description

### Step 2: Upload Files

1. Enter the project → **Files** tab
2. Drag & drop files into the upload area (supports multiple files)
3. Each file shows real-time processing progress (Cleaning → Indexing)
4. Status icons indicate each file's state

**File Status:**

| Icon | Status | Description |
|------|--------|-------------|
| ✅ | Indexed | Index built, ready for precise search |
| 🔄 | Indexing | Building search index |
| ⏳ | Pending | Waiting for indexing |
| ⚠️ | Failed | Index failed, click 🔄 to retry |
| ⏭️ | Skipped | Fast mode, no index needed |

### Step 3: Preview Cleaned Results

1. Switch to the **Preview** tab
2. Select a file from the dropdown
3. View each element with color-coded type badges:

| Badge Color | Element Type | Description |
|-------------|-------------|-------------|
| Blue | Title | Headings |
| Gray | NarrativeText | Body paragraphs |
| Green | Table | Tables |
| Yellow | ListItem | List items |
| Purple | CodeSnippet | Code blocks |
| Pink | Image | Images |

### Step 3.5: Select Files to Include

Each file has a **checkbox** next to it:
- ☑ Checked = included in Generate (default: all checked)
- ☐ Unchecked = excluded, file appears dimmed with strikethrough
- No need to delete files — re-check anytime to include again

### Step 4: Configure Schema & Mode

1. Switch to the **Settings** tab
2. Select a **Schema Template** (e.g., "Software Requirements Spec")
3. Choose **LLM Provider** and **Model**
4. Choose **Extraction Mode**:

| Mode | Description | Best For |
|------|-------------|----------|
| **Fast** | Single LLM call with all document content + full Schema | Small files (< 10 pages), few files |
| **Precise** | Multi-layer Agent + search engine assisted | Large files (> 10 pages), multiple files, high accuracy needed |

5. In Precise mode, optionally enable **LLM Challenge Verification** (see below)
6. Click **Save**
7. Click **Generate Structured Output**

### Step 5: View Output

1. Switch to the **Output** tab
2. Top area shows:
   - **Version selector** (v1, v2, v3...)
   - **Confidence badge** (green ≥80% / yellow 50-80% / red <50%, only with Challenge enabled)
   - **Markdown / JSON** view toggle
3. **Source files** — shows which files were used for this version
4. **Missing fields** (yellow) = LLM couldn't find data
5. **Open questions** (orange) = items flagged for confirmation
6. **Challenges** (purple) = LLM Challenge results, grouped by section with original vs suggested value comparison
7. **Markdown view** — fully rendered (heading levels, table borders, lists, code highlighting)
8. **JSON view** — expandable/collapsible tree structure with syntax highlighting
9. **Copy** or **Download** (.md / .json)

### Step 6: Iterate

- Upload more files or adjust checkboxes → go to Settings → Generate again
- Each generation creates a new version (v1, v2, v3...), never overwrites
- Output tab version dropdown lets you compare outputs from different file selections

---

## 3. Precise Mode Architecture

Precise mode uses a four-layer Agent architecture:

```
Layer 2 (Outline Planning):
  LLM analyzes document summaries → determines which Schema sections have data → plans search keywords

Layer 3 (Per-section Extraction, parallel):
  Each section gets its own LLM call →
  Search engine finds relevant chunks first → LLM extracts only that section's JSON

Layer 4 (LLM Challenge Verification, optional, parallel):
  A second LLM verifies Layer 3 results →
  Finds inconsistencies, contradictions, and suspicious fields → assigns confidence scores

Merge (pure code):
  Combine all sections + challenge results → complete specification document
```

**Advantages:**
- Each LLM call focuses on one topic, higher accuracy
- Search engine assists, handles any document size
- Sections extracted in parallel for speed
- LLM Challenge catches contradictions and errors with per-field confidence

### LLM Challenge Verification (Layer 4)

Enable "LLM Challenge Verification" in Settings (Precise mode only):

1. After Layer 3 extraction, each section is re-verified by a second LLM
2. Results are classified by confidence:

| Confidence | Action | Description |
|-----------|--------|-------------|
| ≥ 80% | ✅ Accept | Both LLMs agree, auto-accepted |
| 50-80% | ⚠️ Flag | Questionable, marked for review |
| < 50% | ❌ Reject | Clear inconsistency |

3. Output tab displays:
   - Overall confidence badge
   - Challenges grouped by section (expandable/collapsible)
   - Original vs Suggested value comparison (red/green columns)

4. Token usage is approximately 1.5x without Challenge (one extra verification call per section)

---

## 4. Supported File Formats

| Format | Extension | Cleaning Capability |
|--------|-----------|-------------------|
| Word | .docx | Heading styles → Title, Lists → ListItem, Table structure |
| PowerPoint | .pptx | Slide Shape type classification, Drawing.Table |
| Excel | .xlsx | Each worksheet → Markdown Table |
| PDF | .pdf | Heuristic classification (title, list, footer detection) |
| HTML | .html | Tags map directly (h1→Title, table→Table) |
| Plain Text | .txt, .md, .csv | Markdown headings, bullets, code fences |
| Images | .png, .jpg, .tiff, .bmp | OCR recognition (requires Tesseract) |

---

## 5. Built-in Schema Templates

### Software Requirements Specification

13 sections:

| Section | Description |
|---------|-------------|
| document | Document metadata (title, version, date, sources) |
| project_overview | Project overview (name, objective, scope, constraints) |
| stakeholders | Stakeholders |
| functional_requirements | Functional requirements (with acceptance criteria, MoSCoW priority) |
| non_functional_requirements | Non-functional requirements (performance, security, availability) |
| data_model | Data model (Entity + Fields) |
| api_endpoints | API endpoint specifications |
| ui_screens | UI screen list |
| timeline | Timeline + milestones |
| budget | Budget breakdown |
| risks | Risk assessment |
| glossary | Glossary |
| open_questions | Open questions (auto-populated by LLM) |

**Custom templates:** Place a JSON file in `Data/schema-templates/` — zero code changes needed.

---

## 6. Progress Log & Token Statistics

During generation, the frontend displays real-time execution logs:

```
Layer 2: Planning extraction for 13 sections...
Layer 2: Found 8/13 sections with data
Layer 3: Extracting project_overview (3 queries)...
Layer 3: project_overview done (1,200 tokens)
Layer 3: Completed 8 sections
Merging results...
✅ Generated v2 (Precise) | 25.3s | 5,050 in + 3,200 out = 8,250 tokens
```

The final line shows total time + input/output token usage.

---

## 7. API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/refinery` | Create project |
| GET | `/api/refinery` | List projects |
| GET | `/api/refinery/{id}` | Get project |
| PUT | `/api/refinery/{id}` | Update project |
| DELETE | `/api/refinery/{id}` | Soft delete |
| GET | `/api/refinery/{id}/files` | List files |
| POST | `/api/refinery/{id}/files` | Upload + clean (SSE) |
| DELETE | `/api/refinery/{id}/files/{fileId}` | Delete file |
| GET | `/api/refinery/{id}/files/{fileId}/preview` | Cleaning preview |
| POST | `/api/refinery/{id}/files/{fileId}/reindex` | Retry indexing (SSE) |
| POST | `/api/refinery/{id}/generate` | Generate structured output (SSE) |
| GET | `/api/refinery/{id}/outputs` | List versions |
| GET | `/api/refinery/{id}/outputs/latest` | Latest version |
| GET | `/api/refinery/{id}/outputs/{version}` | Specific version |
| GET | `/api/schema-templates` | List schema templates |
| GET | `/api/schema-templates/{id}` | Get template details |
