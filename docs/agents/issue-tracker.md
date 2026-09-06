# 議題追蹤器：GitHub

本專案的議題與規格儲存於 GitHub Issues。所有操作均使用 `gh` CLI。

## 慣例

- **建立議題**：`gh issue create --title "..." --body "..."`。多行內容使用 heredoc。
- **讀取議題**：`gh issue view <number> --comments`，並擷取標籤。
- **列出議題**：`gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，並搭配適當的 `--label` 與 `--state` 篩選。
- **新增留言**：`gh issue comment <number> --body "..."`
- **新增／移除標籤**：`gh issue edit <number> --add-label "..."` ／ `--remove-label "..."`
- **關閉議題**：`gh issue close <number> --comment "..."`

目前尚未設定 Git 遠端。連結 GitHub 遠端後，`gh` 會自動辨識儲存庫。

## Pull Request 作為分流來源

**PRs as a request surface: no.** _(若本專案將外部 PR 視為功能請求，請改為 `yes`；`/triage` 會讀取此旗標。)_

設定為 `yes` 時，PR 會與議題共用相同的標籤與狀態，並使用對應的 `gh pr` 指令：

- **讀取 PR**：`gh pr view <number> --comments`，並使用 `gh pr diff <number>` 讀取差異。
- **列出待分流的外部 PR**：`gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`，只保留 `authorAssociation` 為 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE` 的項目。
- **留言／標籤／關閉**：`gh pr comment`、`gh pr edit --add-label`／`--remove-label`、`gh pr close`。

GitHub 的 issue 和 PR 共用編號空間，因此裸用的 `#42` 可能是任一者：先用 `gh pr view 42` 解析，失敗後再用 `gh issue view 42`。

## 當技能要求「發布至議題追蹤器」

建立 GitHub Issue。

## 當技能要求「取得相關工作單」

執行 `gh issue view <number> --comments`。

## Wayfinding 操作

供 `/wayfinder` 使用。**地圖**是一個 GitHub Issue；**子工作單**是其 child issues。

- **地圖**：建立帶有 `wayfinder:map` 標籤的單一 Issue，用來記錄 Notes、Decisions-so-far 與 Fog：`gh issue create --label wayfinder:map`。
- **子工作單**：以 GitHub sub-issue 連結至地圖，標籤使用 `wayfinder:<type>`（`research`、`prototype`、`grilling` 或 `task`）。若未啟用 sub-issues，於地圖內容加入 task list，並在子工作單開頭寫入 `Part of #<map>`。認領後，指派給目前的開發者。
- **阻擋關係**：優先使用 GitHub 原生 issue dependencies。若無法使用，於子工作單開頭寫入 `Blocked by: #<n>, #<n>`。所有阻擋項目關閉後，工作單才算解除阻擋。
- **前線查詢**：列出地圖的未關閉子工作單，排除尚有阻擋關係或已被指派者；依地圖順序取第一項。
- **認領**：`gh issue edit <n> --add-assignee @me`，作為工作階段的第一個寫入操作。
- **完成**：`gh issue comment <n> --body "<answer>"`，接著執行 `gh issue close <n>`，再將 context 指標附加至地圖的 Decisions-so-far。
