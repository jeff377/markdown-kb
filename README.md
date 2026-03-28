# MarkdownKB

A Markdown knowledge base viewer powered by GitHub Repositories, built with ASP.NET Core (.NET 10).

## Quick Start (Docker)

### 1. Start the service

```bash
docker compose up -d
```

### 2. Open your browser

```
http://localhost:8080
```

### 3. Browse a Repository

**Option A: Direct URL**

Append `{owner}/{repo}` to the base URL:

```
http://localhost:8080/jeff377/markdown-kb-content
```

You can also link directly to a specific file:

```
http://localhost:8080/jeff377/markdown-kb-content/docs/intro.md
```

**Option B: Home page form**

Fill in the form on the home page:

| Field | Example | Description |
|-------|---------|-------------|
| Owner | `jeff377` | GitHub username or organization |
| Repository | `markdown-kb-content` | Repository name |
| GitHub Token | *(leave blank)* | Not required for public repos |

Click **Browse** to enter the document viewer.

---

## Private Repository Access

To browse a private repository, provide a GitHub Personal Access Token with `repo` read permission:

1. Go to [GitHub Settings → Developer settings → Personal access tokens](https://github.com/settings/tokens)
2. Choose **Fine-grained tokens** (recommended) or **Tokens (classic)**
3. Grant **Contents: Read** permission for the target repository
4. Copy the generated token (format: `github_pat_...` or `ghp_...`)
5. Paste it into the **GitHub Token** field on the home page

The token is encrypted and stored in a browser cookie (valid for 7 days). You won't need to re-enter it on subsequent visits. To update the token, simply paste a new value into the field.

---

## Stop the Service

```bash
docker compose down
```

> **Note:** The `dp_keys` volume stores ASP.NET Core Data Protection keys.
> Running `docker compose down -v` will delete this volume, invalidating existing cookies and requiring users to re-enter their tokens.

---

## Local Development

```bash
cd src/MarkdownKB.Web
dotnet run
```

Listens on `http://localhost:5275` by default (or `https://localhost:7195` for HTTPS).
