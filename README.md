<img width="750" height="400" alt="image" src="https://github.com/user-attachments/assets/c5d43325-d024-4e51-ab49-9ed609285d02" />
<img width="750" height="400" alt="image" src="https://github.com/user-attachments/assets/2dd45299-84a7-4410-8392-cf0f1bf5f010" />

# Redis Viewer

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Redis](https://img.shields.io/badge/Redis-Client-DC382D?logo=redis&logoColor=white)](https://redis.io/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub stars](https://img.shields.io/github/stars/0xZunia/RedisViewer?style=social)](https://github.com/0xZunia/RedisViewer)

A modern, web-based Redis client built with Blazor Server and MudBlazor. Browse and manage your Redis keys with a clean, intuitive interface.

## Features

- Connect to any Redis server with optional password authentication
- Browse keys organized by type (Strings, Hashes, Other)
- View and edit string values with JSON syntax highlighting
- View and edit hash fields
- View lists, sets, and sorted sets
- Real-time key streaming with pattern filtering
- Dark/Light theme toggle
- Server info display (version, memory usage, uptime)

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
5. Click **Connect**

Once connected, you can:
- Filter keys using patterns (e.g., `user:*`, `session:*`)
- Click on any key to view its value
- Edit string values or hash fields directly
- Delete keys as needed

## Tech Stack

- [ASP.NET Core 8](https://docs.microsoft.com/aspnet/core) - Web framework
- [Blazor Server](https://docs.microsoft.com/aspnet/core/blazor) - Interactive UI
- [MudBlazor](https://mudblazor.com/) - Material Design component library
- [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) - Redis client

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

**[Reyan CARLIER](https://github.com/0xZunia/)**
