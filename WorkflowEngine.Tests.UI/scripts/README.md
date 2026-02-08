# WorkflowEngine.Tests.UI – run scripts

Run from `WorkflowEngine.Tests.UI` (or pass correct paths from repo root).

| Script | Description |
|--------|-------------|
| `start-backend.sh` | Starts ASP.NET Core backend at http://localhost:5186 |
| `start-frontend.sh` | Installs deps if needed, runs Vite dev server at http://localhost:5173 |
| `start-all.sh` | Starts both in background; Ctrl+C stops both |

**Debug in Cursor/VS Code:** use Run and Debug (F5) and choose:

- **Debug Backend** – build + run backend under debugger
- **Debug Frontend (Chrome)** – start Vite + attach Chrome with source maps
- **Debug Full Stack** – run both and debug backend + frontend

Configs live in repo root `.vscode/launch.json` and `.vscode/tasks.json`.
