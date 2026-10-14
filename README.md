# CameraDataSync

A .NET background application that keeps **ANPR cameras** and the **web application** in sync.

It pulls vehicle **IN / OUT** events (with images and date-time) from the cameras and sends them to the web application. It also pushes vehicle changes made in the web app (add, delete, inactive) back to the cameras automatically, with a sync cycle of **every 30 seconds**.

---

## Features

### Camera to Web App
- Fetches vehicle **IN** and **OUT** entries from the camera
- Captures the **vehicle image** for each entry
- Records the **date and time** of every entry and exit
- Identifies each vehicle as **Registered** or **Unregistered**
- Saves all data to the web application through its API

### Web App to Camera
- **New vehicle added** in the web app is added to the camera automatically
- **Vehicle deleted** in the web app is removed from the camera automatically
- **Vehicle made inactive** in the web app is removed or disabled on the camera automatically

### Auto Sync (Background Service Worker)
- Runs as a **background service worker**, so no user interface or manual action is needed
- Starts automatically with the application and keeps running continuously
- Syncs data every **30 seconds** in a repeating loop
- Handles errors without stopping: if the camera or API is unreachable, it logs the error and retries in the next cycle
- A separate worker (`ImageFetchWorker`) downloads vehicle images in the background, so image downloads do not slow down the main sync

---

## How It Works

```
        +-------------+     IN / OUT + image + datetime     +-------------+
        |   Camera    | ----------------------------------> |   Web App   |
        |   (ANPR)    |                                     |   (API)     |
        |             | <---------------------------------- |             |
        +-------------+    Add / Delete / Inactive vehicle  +-------------+
                       \                                   /
                        \      CameraDataSync (30 sec)     /
                         +---------------------------------+
```

1. The app logs in to the web API and gets an auth token.
2. Every 30 seconds it:
   - reads new IN / OUT records and images from the camera
   - matches each plate against the registered vehicle list
   - posts the records to the web app as **Registered** or **Unregistered**
   - checks the web app for vehicles that were added, deleted or made inactive
   - applies those changes to the camera
3. Image downloads run in a separate background worker (`ImageFetchWorker`).

---

## Tech Stack

- C# / .NET
- ASP.NET Core Web API (web application side)
- `HttpClient` for camera and API communication
- SQL Server (web application database)
- Background worker / hosted service for scheduled sync

---

## Project Structure

```
ApiTaskApp/
├── Program.cs              # Entry point, sync loop, API and camera calls
├── ImageFetchWorker.cs     # Background worker that downloads vehicle images
├── ANPRDataAccess          # Data access for ANPR / vehicle records
├── Models/                 # Vehicle and event models
├── appsettings.json        # Configuration (URLs, credentials, interval)
└── README.md
```

---

## Getting Started

### Prerequisites
- Visual Studio 2022 or later (or the .NET SDK)
- Network access to the camera and to the web application API
- SQL Server database

### 1. Clone the repository
```bash
git clone https://github.com/prashantpawarids-dot/CameraDataSync.git
cd CameraDataSync
```

### 2. Configure the app
Open `appsettings.json` and update the values for your environment:

```json
{
  "BaseUrl": "https://your-webapp-api-url",
  "AuthUrl": "https://your-webapp-api-url/api/auth/login",
  "Camera": {
    "Ip": "192.168.x.x",
    "Username": "your-camera-username",
    "Password": "your-camera-password"
  },
  "SyncIntervalSeconds": 30,
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=YourDb;Trusted_Connection=True;"
  }
}
```

> **Do not commit real passwords or API keys to GitHub.** Keep secrets in environment variables, User Secrets or a private config file.

### 3. Build and run
```bash
dotnet restore
dotnet build
dotnet run
```

Or open the solution in Visual Studio and press **F5**.

---

## Running as a Background Service

For production, run the worker continuously on the server instead of from Visual Studio.

### Option 1: Windows Service
```bash
dotnet publish -c Release -o C:\Services\CameraDataSync

sc create CameraDataSync binPath= "C:\Services\CameraDataSync\ApiTaskApp.exe" start= auto
sc start CameraDataSync
```

To stop or remove it:
```bash
sc stop CameraDataSync
sc delete CameraDataSync
```

> This needs the `Microsoft.Extensions.Hosting.WindowsServices` package and `UseWindowsService()` in `Program.cs`.

### Option 2: Console window
```bash
dotnet run -c Release
```
Keep the window open. Closing it stops the sync.

Because the service is set to start automatically, it comes back up after a server restart.

---

## Sync Rules

| Action in Web App | What happens on Camera | Time |
|---|---|---|
| Add new vehicle | Vehicle is added to the camera | within 30 sec |
| Delete vehicle | Vehicle is removed from the camera | within 30 sec |
| Make vehicle inactive | Vehicle is removed or disabled on the camera | within 30 sec |

| Event on Camera | What happens in Web App |
|---|---|
| Vehicle IN | Entry saved with image and date-time |
| Vehicle OUT | Exit saved with image and date-time |
| Plate found in vehicle list | Marked as **Registered** |
| Plate not found in vehicle list | Marked as **Unregistered** |

---

## Troubleshooting

- **No data syncing:** check that the camera IP is reachable and the web API URL is correct.
- **401 / 403 from API:** check the login credentials in `appsettings.json`.
- **Images not saving:** check that `ImageFetchWorker` is running and has write access to the image folder.
- **Vehicle not reaching camera:** wait up to 30 seconds, then check the app logs.

---

## Contributing

1. Pull the latest code: `git pull`
2. Make your changes
3. Commit: `git commit -m "Describe your change"`
4. Push: `git push`

---

## Author

**Prashant Pawar**
GitHub: [@prashantpawarids-dot](https://github.com/prashantpawarids-dot)
