# 系統架構概述

本平台採用分層架構設計，將前端展示、業務邏輯、資料存取明確分離，提升可維護性與可測試性。

## 前端層

前端使用 ASP.NET Core Razor Pages 實作，搭配 Bootstrap 5 提供響應式介面。
每個頁面對應一個 PageModel，負責處理 HTTP 請求與資料繫結。
靜態資源（CSS、JavaScript、圖片）統一放置於 wwwroot 目錄，透過 CDN 加速分發。

使用者瀏覽器發出請求後，由 Nginx 反向代理接收，轉發至 ASP.NET Core 應用程式。
應用程式處理完成後回傳 HTML，由瀏覽器渲染顯示。

## 業務邏輯層

業務邏輯集中在 Core 專案的 Services 目錄，採用相依注入方式提供給上層使用。
主要服務包含：GitHubService 負責與 GitHub API 溝通、MarkdownService 負責文件渲染、
TokenService 負責 API Token 的加密儲存與讀取。

服務之間透過介面（interface）定義契約，方便抽換實作與單元測試。
所有服務均設計為無狀態（stateless），確保在水平擴展時行為一致。

## 資料存取層

Phase 2 新增的搜尋功能使用 PostgreSQL + pgvector 作為向量資料庫。
DocumentChunk 實體對應資料庫中的 document_chunks 資料表，透過 EF Core 進行 CRUD。
Embedding 向量由 OpenAI text-embedding-3-small 模型產生，維度為 1536。

資料存取採用 Repository Pattern，將 SQL 查詢封裝於 Service 層，
避免業務邏輯直接耦合資料庫操作。

## 部署架構

整個應用以 Docker Compose 編排，包含 web 與 db 兩個服務。
web 服務基於官方 .NET 10 runtime image，db 服務使用 pgvector/pgvector:pg16。
兩者透過 Docker 內部網路溝通，僅 web 服務對外暴露 8080 port。

生產環境建議搭配 Nginx 作為反向代理，處理 TLS 終止與靜態資源快取。
