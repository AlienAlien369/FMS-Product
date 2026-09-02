# Freebuff Frontend Preview

## How to reproduce uncommitted artifacts
No special env files needed — the frontend uses only `vite.config.ts` (committed).

Dependencies are already installed in `frontend/node_modules`.

## How to run the server
```bash
cd frontend
npm run dev
```

Vite serves on **port 5173** by default (configured in `vite.config.ts`).
The API proxy forwards `/api/*` to `http://localhost:8080`.

### Windows detached (PowerShell)
```powershell
Start-Process -FilePath 'npm.cmd' `
  -ArgumentList 'run','dev' `
  -RedirectStandardOutput '<log>' `
  -RedirectStandardError '<log>.err' `
  -WindowStyle Hidden
```
