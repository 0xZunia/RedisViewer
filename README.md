<img width="750" height="400" alt="image" src="https://github.com/user-attachments/assets/c5d43325-d024-4e51-ab49-9ed609285d02" />
<img width="750" height="400" alt="image" src="https://github.com/user-attachments/assets/2dd45299-84a7-4410-8392-cf0f1bf5f010" />

# Redis Viewer

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Redis](https://img.shields.io/badge/Redis-Client-DC382D?logo=redis&logoColor=white)](https://redis.io/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub stars](https://img.shields.io/github/stars/0xZunia/RedisViewer?style=social)](https://github.com/0xZunia/RedisViewer)

A modern, feature-rich web-based Redis client built with Blazor Server and MudBlazor. Browse, manage, and monitor your Redis databases with a clean, intuitive interface.

## Features

### Core Functionality
- **Multi-database support** - Switch between Redis databases (0-15)
- **Connection profiles** - Save and manage multiple connection configurations
- **Full CRUD operations** - Create, read, update, and delete keys of all types
- **Pattern filtering** - Filter keys using Redis patterns (e.g., `user:*`, `session:*`)
- **Search in values** - Search across key values, not just key names
- **Real-time streaming** - Live key loading with progress indication

### Data Type Support
- **Strings** - View and edit with JSON syntax highlighting
- **Hashes** - Add, edit, and delete individual fields
- **Lists** - Add items (LPUSH/RPUSH), edit by index, remove items
- **Sets** - Add and remove members
- **Sorted Sets** - Add members with scores, edit scores, remove members

### Key Management
- **TTL Management** - Set or remove expiration on keys
- **Rename keys** - Rename any key
- **Duplicate keys** - Clone keys with all their data and TTL
- **Export/Import** - Export keys to JSON, import from JSON
- **Bulk operations** - Multi-select keys for bulk delete or bulk TTL
- **Favorites** - Star frequently used keys for quick access

### Monitoring & Tools
- **Redis Console** - Execute raw Redis commands directly
- **Key Statistics** - View key count by type
- **Slow Log Viewer** - Monitor slow queries
- **Client List** - View connected clients
- **Pub/Sub Testing** - Subscribe to channels and publish messages
- **Key Comparison** - Compare two keys side-by-side

### User Experience
- **Dark/Light theme** - Toggle with persistent preference
- **Tree view** - Organize keys by namespace (colon-separated)
- **Keyboard shortcuts** - Quick actions for power users
- **Copy to clipboard** - One-click copy for keys and values
- **Pagination** - Handle large collections efficiently
- **JSON highlighting** - VS Code-style syntax highlighting for JSON values

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl + N` | Create new key |
| `Ctrl + R` | Refresh keys |
| `Ctrl + `` ` | Toggle Redis console |
| `Delete` | Delete selected key |
| `Escape` | Close dialogs |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A Redis server to connect to

## Getting Started

### Clone the repository

```bash
git clone https://github.com/0xZunia/RedisViewer.git
cd RedisViewer
```

### Run the application

```bash
cd RedisViewer
dotnet run
```

The application will start at `https://localhost:7042` or `http://localhost:5202`.

### Build for production

```bash
dotnet publish -c Release
```

## Usage

1. Launch the application
2. Enter your Redis server host (default: `localhost`)
3. Enter the port (default: `6379`)
4. Enter password if required
5. Click **Connect** (or save as a profile for quick access later)

Once connected, you can:
- Select a database (0-15) from the dropdown
- Filter keys using patterns or search in values
- Toggle tree view to organize keys by namespace
- Click on any key to view and edit its value
- Use the monitoring menu for stats, slow log, clients, console, and pub/sub
- Star keys to add them to favorites
- Multi-select keys for bulk operations

## Tech Stack

- [ASP.NET Core 9](https://docs.microsoft.com/aspnet/core) - Web framework
- [Blazor Server](https://docs.microsoft.com/aspnet/core/blazor) - Interactive UI
- [MudBlazor](https://mudblazor.com/) - Material Design component library
- [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) - Redis client

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

**[Reyan CARLIER](https://github.com/0xZunia/)**
