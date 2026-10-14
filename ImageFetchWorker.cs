using ApiTaskApp;
using System.Text.Json;
using System.Collections.Concurrent;

public static class ImageFetchWorker
{
    private static readonly HttpClient sharedClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, (string Token, DateTime Expiry)> tokenCache = new();
    private const int MaxParallelPerCamera = 12;
    private const int MaxRetries = 5;

    // PRODUCTION: saare cameras, poora backlog, parallel
    public static async Task FlushAllCamerasAsync(string connectionString, string clientId, string clientSecret)
    {
        List<ANPR_Controller> cameras = await Program.GetANPRList();
        Console.WriteLine($"=== FLUSH START: {cameras.Count} camera(s) ===");

        foreach (var camera in cameras)
        {
            if (!IsPingSuccessful(camera.ANPRIP))
            {
                Console.WriteLine($"{camera.ANPRIP}: OFFLINE - skipped.");
                continue;
            }

            int totalDone = 0, totalFailed = 0;
            int round = 0;

            while (true)
            {
                round++;
                var (done, failed, processed) = await ProcessOneCameraBatchAsync(camera, connectionString, clientId, clientSecret, 500);
                totalDone += done;
                totalFailed += failed;

                Console.WriteLine($"{camera.ANPRIP} round {round}: {processed} processed ({done} done, {failed} failed). Running totals: {totalDone} done, {totalFailed} failed.");

                if (processed == 0) break; // is camera ka backlog khatam (permanently-failed bhi ab exclude ho jaate hain)
            }

            Console.WriteLine($"=== {camera.ANPRIP} COMPLETE: {totalDone} done, {totalFailed} failed-this-run ===\n");
        }

        Console.WriteLine("=== FLUSH ALL CAMERAS COMPLETE ===");
    }

    public static async Task<(int done, int failed, int processed)> ProcessOneCameraBatchAsync(
    ANPR_Controller camera, string connectionString, string clientId, string clientSecret, int batchSize, bool todayOnly = false)
    {
        string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";
        string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";

        string token = await GetCachedTokenAsync(camera.ANPRIP, authUrl, clientId, clientSecret);
        if (string.IsNullOrEmpty(token)) return (0, 0, 0);

        var dataAccess = new ANPRDataAccess(connectionString);

        // Today-loop aur Backlog-loop kabhi ek doosre ka data touch nahi karte - date se clean split
        string dateFilter = todayOnly
            ? "AND CAST(v.InoutTime AS DATE) = CAST(GETDATE() AS DATE) "
            : "AND CAST(v.InoutTime AS DATE) < CAST(GETDATE() AS DATE) ";

        // Permanently-failed (RetryCount >= 5) records ko skip karo - infinite loop se bachne ke liye
        var pending = dataAccess.GetDataTable(
     $"SELECT TOP {batchSize} v.unique_id, v.VehicleNumber FROM vwGetVehicleAccessImageUpdateList v " +
     $"WHERE v.Cameraid = {camera.ID} " +
     dateFilter +
     $"AND v.unique_id NOT IN (SELECT unique_id FROM tblANPR_TRImages WHERE Status='FAILED' AND RetryCount >= {MaxRetries} AND Imagetype='STATUS') " +
     $"ORDER BY v.InoutTime ASC");

        if (pending == null || pending.Rows.Count == 0) return (0, 0, 0);

        int doneCount = 0, failCount = 0;
        var semaphore = new SemaphoreSlim(MaxParallelPerCamera);
        var tasks = pending.Rows.Cast<System.Data.DataRow>().Select(async row =>
        {
            await semaphore.WaitAsync();
            try
            {
                bool ok = await FetchOneImageAsync(baseUrl, token, row["unique_id"].ToString(),
     row["VehicleNumber"].ToString(), camera.ID.ToString(), connectionString);
                if (ok) Interlocked.Increment(ref doneCount);
                else Interlocked.Increment(ref failCount);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        return (doneCount, failCount, pending.Rows.Count);
    }

    private static async Task<bool> FetchOneImageAsync(string baseUrl, string token, string uniqueId, string plate, string cameraId, string connectionString)
    {
        var dataAccess = new ANPRDataAccess(connectionString);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "event-image");
            request.Headers.Add("Authorization", "Token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            request.Content = new StringContent($"{{\"message_id\": \"{uniqueId}\"}}", null, "application/json");

            var response = await sharedClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                await MarkStatusAsync(connectionString, uniqueId, "FAILED");
                return false;
            }

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            var images = doc.RootElement.GetProperty("image_urls");

            bool anySaved = false;
            foreach (var itm in images.EnumerateArray())
            {
                string url = itm.GetProperty("url").GetString();
                string type = itm.GetProperty("type").GetString();
                byte[] imgBytes = await sharedClient.GetByteArrayAsync(url);

                var ev = new ANPRDataAccess.VehicleEvent
                {
                    UniqueId = uniqueId,
                    PlateNumber = plate,
                    CameraId = cameraId,
                    ImageUrl = url,
                    ImageType = type,
                    Timestamp = DateTime.Now
                };
                await dataAccess.InsertIntoDatabase(ev, imgBytes);
                anySaved = true;
            }

            if (anySaved)
            {
                await MarkStatusAsync(connectionString, uniqueId, "DONE");
                return true;
            }
            await MarkStatusAsync(connectionString, uniqueId, "FAILED");
            return false;
        }
        catch
        {
            try { await MarkStatusAsync(connectionString, uniqueId, "FAILED"); } catch { }
            return false;
        }
    }


    // AAJ KA DATA: har 15 sec check, turant sync
    public static async Task TodaySyncLoopAsync(string connectionString, string clientId, string clientSecret)
    {
        while (true)
        {
            try
            {
                var cameras = await Program.GetANPRList();
                var online = cameras.Where(c => IsPingSuccessful(c.ANPRIP)).ToList();

                var tasks = online.Select(async camera =>
                {
                    var (done, failed, processed) = await ProcessOneCameraBatchAsync(
                        camera, connectionString, clientId, clientSecret, 100, todayOnly: true);
                    if (processed > 0)
                        Console.WriteLine($"[TodaySync] {camera.ANPRIP}: {processed} processed ({done} done, {failed} failed).");
                });
                await Task.WhenAll(tasks);
            }
            catch (Exception ex) { Console.WriteLine("[TodaySync] error: " + ex.Message); }

            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }

    // PURANA BACKLOG: continuously drain hoga, aaj ke data ko block nahi karega
    public static async Task BacklogDrainLoopAsync(string connectionString, string clientId, string clientSecret)
    {
        while (true)
        {
            try
            {
                var cameras = await Program.GetANPRList();
                var online = cameras.Where(c => IsPingSuccessful(c.ANPRIP)).ToList();

                var tasks = online.Select(async camera =>
                {
                    var (done, failed, processed) = await ProcessOneCameraBatchAsync(
                        camera, connectionString, clientId, clientSecret, 300, todayOnly: false);
                    if (processed > 0)
                        Console.WriteLine($"[BacklogDrain] {camera.ANPRIP}: {processed} processed ({done} done, {failed} failed).");
                });
                await Task.WhenAll(tasks);
            }
            catch (Exception ex) { Console.WriteLine("[BacklogDrain] error: " + ex.Message); }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<string> GetCachedTokenAsync(string ip, string authUrl, string clientId, string clientSecret)
    {
        if (tokenCache.TryGetValue(ip, out var cached) && cached.Expiry > DateTime.Now)
            return cached.Token;

        string token = await Program.GetTokenAsync(authUrl, clientId, clientSecret);
        if (!string.IsNullOrEmpty(token))
            tokenCache[ip] = (token, DateTime.Now.AddMinutes(15));
        return token;
    }

    // FIX: pehle sirf UPDATE tha jo FAILED case mein 0 rows affect karta tha (row exist hi nahi karti thi)
    // isliye wahi records baar-baar retry hote the - infinite loop. Ab row na ho to INSERT karte hain.
    private static async Task MarkStatusAsync(string connectionString, string uniqueId, string status)
    {
        var dataAccess = new ANPRDataAccess(connectionString);
        string safeId = uniqueId.Replace("'", "''");

        // Imagetype='STATUS' se tag karte hain taaki yeh row kabhi bhi real PLATE/FRAME image row se collide na kare
        dataAccess.GetDataTable($@"
            IF EXISTS (SELECT 1 FROM tblANPR_TRImages WHERE unique_id = '{safeId}' AND Imagetype = 'STATUS')
                UPDATE tblANPR_TRImages 
                SET Status = '{status}', RetryCount = RetryCount + 1, LastAttemptOn = GETDATE() 
                WHERE unique_id = '{safeId}' AND Imagetype = 'STATUS'
            ELSE
                INSERT INTO tblANPR_TRImages (unique_id, Imagetype, Status, RetryCount, LastAttemptOn)
                VALUES ('{safeId}', 'STATUS', '{status}', 1, GETDATE())
        ");
    }

    // Chhote test ke liye
    public static async Task RunTestAsync(string connectionString, string clientId, string clientSecret, string testCameraIP, int testLimit = 20)
    {
        var camList = await Program.GetANPRList();
        var camera = camList.FirstOrDefault(c => c.ANPRIP == testCameraIP);
        if (camera == null) { Console.WriteLine("Camera not found."); return; }
        var (done, failed, processed) = await ProcessOneCameraBatchAsync(camera, connectionString, clientId, clientSecret, testLimit);
        Console.WriteLine($"Test complete: {processed} processed, {done} done, {failed} failed.");
    }

    static bool IsPingSuccessful(string ip)
    {
        try { using var p = new System.Net.NetworkInformation.Ping(); return p.Send(ip, 1000).Status == System.Net.NetworkInformation.IPStatus.Success; }
        catch { return false; }
    }
}