using ApiTaskApp;
using Azure;
using Azure.Core;
using Microsoft.Extensions.Configuration;
//using Microsoft.IdentityModel.Tokens;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using static ApiTaskApp.ANPRDataAccess;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;
using JsonSerializer = System.Text.Json.JsonSerializer;

class Program
{
    private static readonly HttpClient client = new HttpClient();
    private static IConfiguration _config;
    private static string baseUrl = "";
    private static string authUrl = "";
    private static string clientId = "";
    private static string clientSecret = "";
    //private static string connectionString = "";

    internal static string connectionString = "";

    private static string _AnprVeh = "";
    private static int updatedID = 0; private static int insertedID = 0;
    static string token = "";

    // Define the log file path (you can change this to a path on your server)
    public static string logFilePath = @"C:\Logs\ApiLog.txt";


    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting API calls with tasks...set config");
        //string  dwnDt = Convert.ToDateTime(DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss");

        _config = new ConfigurationBuilder()
          .SetBasePath(Directory.GetCurrentDirectory())
          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
          .Build();

        // Define the log file path (you can change this to a path on your server)
        logFilePath = Directory.GetCurrentDirectory().ToString() + @"\Logs\ApiLog.txt";

        // Make sure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));

        Console.WriteLine("Running Path : " + Directory.GetCurrentDirectory().ToString());

        // Load config from appsettings.json        
        connectionString = _config["connectionString:defaultconnection"].ToString();
        baseUrl = _config["AppSettings:ApiBaseUrl"].ToString();
        authUrl = _config["AppSettings:ApiAuthUrl"].ToString();
        clientId = _config["AppSettings:ClientId"].ToString();
        clientSecret = _config["AppSettings:ClientSecret"].ToString();

        //       baseUrl = "http://192.168.31.7/api/v1/sdk/";
        //CallGetApiAsync | CallPostApiListAsync |  DeleteBulkVehicleListAsync


        // _ = ScheduleTask("CallGetApiAsync", TimeSpan.FromMinutes(10));
        //_ = ScheduleTask("CallPostApiListAsync", TimeSpan.FromMinutes(20));
        //_ = ScheduleTask("GetVehicleEventListAsync", TimeSpan.FromMinutes(20));

        //_ = ScheduleTask("DeleteBulkVehicleListAsync", TimeSpan.FromMinutes(120));

        //only list getting
        // _ = ScheduleTask("GetVehicleAsync", TimeSpan.FromMinutes(50));

        // }

        // Console.WriteLine("All tasks completed.");
        //  Console.ReadLine(); // keep running


        //if (args.Length > 0 && args[0] == "--backfill")
        //{
        //    DateTime fromDate = args.Length > 1 ? DateTime.Parse(args[1]) : new DateTime(2026, 5, 1);
        //    DateTime toDate = args.Length > 2 ? DateTime.Parse(args[2]) : DateTime.Now.AddHours(-1);
        //    Console.WriteLine($"Starting historical backfill from {fromDate} to {toDate}...");
        //    await BackfillHistoricalDataAsync(fromDate, toDate);
        //    Console.WriteLine("Backfill finished. Exiting.");
        //    return; // does not start the live-sync loop
        //}


        if (args.Length > 0 && args[0] == "--backfill")
        {
            DateTime fromDate = args.Length > 1 ? DateTime.Parse(args[1]) : new DateTime(2026, 5, 1);
            DateTime toDate = args.Length > 2 ? DateTime.Parse(args[2]) : DateTime.Now.AddHours(-1);
            Console.WriteLine($"Starting historical backfill from {fromDate} to {toDate}...");
            await BackfillHistoricalDataAsync(fromDate, toDate);
            Console.WriteLine("Backfill finished. Exiting.");
            return; // does not start the live-sync loop
        }




        if (args.Length > 1 && args[0] == "--reconcile")
        {
            string neighbourhood = args[1];
            string onlyIP = args.Length > 2 ? args[2].Trim() : null; // optional: ek specific camera IP

            Console.WriteLine($"Starting manual reconcile for '{neighbourhood}'" + (onlyIP != null ? $" (ONLY camera {onlyIP})" : "") + "...");

            List<ANPR_Controller> allCameras = await GetCameraAsync();
            var targetCameras = allCameras
                .Where(c => !string.IsNullOrEmpty(c.ANPRLocation)
                            && c.ANPRLocation.Trim().Equals(neighbourhood, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (onlyIP != null)
            {
                targetCameras = targetCameras.Where(c => c.ANPRIP.Trim() == onlyIP).ToList();
            }

            if (targetCameras.Count == 0)
            {
                Console.WriteLine($"No matching camera(s) found for '{neighbourhood}'" + (onlyIP != null ? $" with IP {onlyIP}" : "") + ".");
                return;
            }

            Console.WriteLine($"Found {targetCameras.Count} camera(s) to reconcile.");

            foreach (var camera in targetCameras)
            {
                if (!IsPingSuccessful(camera.ANPRIP))
                {
                    Console.WriteLine($"Camera {camera.ANPRIP}: OFFLINE - skipped.");
                    continue;
                }
                Console.WriteLine($"Reconciling camera {camera.ANPRIP}...");
                await ReconcileCameraToMasterListAsync(camera, neighbourhood);
            }

            Console.WriteLine("Manual reconcile finished. Exiting.");
            return;
        }
        //if (args.Length > 1 && args[0] == "--upload-named")

        if (args.Length > 0 && args[0] == "--cleanup-owners")
        {
            string onlyIP = args.Length > 1 ? args[1].Trim() : null;
            List<ANPR_Controller> allCameras = await GetCameraAsync();
            var targets = onlyIP != null ? allCameras.Where(c => c.ANPRIP.Trim() == onlyIP).ToList() : allCameras;
            foreach (var camera in targets)
            {
                if (!IsPingSuccessful(camera.ANPRIP)) { Console.WriteLine($"{camera.ANPRIP} OFFLINE - skipped."); continue; }
                Console.WriteLine($"Cleaning up owners on {camera.ANPRIP}...");
                await CleanupOrphanedOwnersAsync(camera);
            }
            Console.WriteLine("Cleanup finished. Exiting.");
            return;
        }
        if (args.Length > 1 && args[0] == "--upload-named")
        {
            string neighbourhood = args[1];
            string onlyIP = args.Length > 2 ? args[2].Trim() : null;

            Console.WriteLine($"Starting NAMED upload for '{neighbourhood}'" + (onlyIP != null ? $" (ONLY camera {onlyIP})" : "") + "...");

            List<ANPR_Controller> allCameras = await GetCameraAsync();
            var targetCameras = allCameras
                .Where(c => !string.IsNullOrEmpty(c.ANPRLocation)
                            && c.ANPRLocation.Trim().Equals(neighbourhood, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (onlyIP != null)
                targetCameras = targetCameras.Where(c => c.ANPRIP.Trim() == onlyIP).ToList();

            if (targetCameras.Count == 0)
            {
                Console.WriteLine("No matching camera(s) found.");
                return;
            }

            foreach (var camera in targetCameras)
            {
                if (!IsPingSuccessful(camera.ANPRIP))
                {
                    Console.WriteLine($"Camera {camera.ANPRIP}: OFFLINE - skipped.");
                    continue;
                }
                await UploadVehiclesWithOwnerNamesAsync(camera, neighbourhood);
            }

            Console.WriteLine("Named upload finished. Exiting.");
            return;
        }

        //if (args.Length > 0 && args[0] == "--flush-all-images")
        //{
        //    await ImageFetchWorker.FlushAllCamerasAsync(connectionString, clientId, clientSecret);
        //    return;
        //}


        //while (true)
        //{


        if (args.Length > 0 && args[0] == "--flush-all-images")
        {
            await ImageFetchWorker.FlushAllCamerasAsync(connectionString, clientId, clientSecret);
            return;
        }

        // Yeh 2 loops permanently background mein chalenge - Console band hone tak
        _ = Task.Run(() => ImageFetchWorker.TodaySyncLoopAsync(connectionString, clientId, clientSecret));
        _ = Task.Run(() => ImageFetchWorker.BacklogDrainLoopAsync(connectionString, clientId, clientSecret));

        //}
        while (true)
        {
            try
            {
                await ScheduleTask("CallGetApiAsync", TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL LOOP ERROR - restarting in 30s: " + ex);
                LogApiRequest("FATAL LOOP ERROR - restarting in 30s: " + ex, logFilePath);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
    }


    /* 
     Console.WriteLine("Fetching token...");
        string token = await GetTokenAsync(authUrl, clientId, clientSecret);
        //if (token == null)
        //    token = "123";
        Console.WriteLine("Calling API with token complete...");
        if (token != null)
        {
            // Define multiple tasks http://192.168.0.104/api/v1/sdk/login..../vehicle
            //ANPR Registration CallGetApiAsync - read from ANPR || ANPR Registration CallPostApiListAsync - read from ANPR  
            Console.WriteLine("Independent parallel schedulers started...");

        }
        else
            Console.WriteLine("Unable to Get Token.");
     */

    // Convert DataTable to List<Device>
    static List<ANPR_Controller> ConvertToDeviceList(DataTable table)
    {
        List<ANPR_Controller> list = new List<ANPR_Controller>();

        foreach (DataRow row in table.Rows)
        {
            ANPR_Controller device = new ANPR_Controller
            {
                ID = Convert.ToInt32(row["Id"]),
                ANPRTerminalID = row["ANPRTerminalID"].ToString(),
                ANPRIP = row["ANPRIP"].ToString(),
                ANPRport = row["ANPRport"].ToString(),
                ANPRLocation = row["ANPRLocation"].ToString(),
                isActive = Convert.ToBoolean(row["isActive"]),
                IsDeleted = Convert.ToBoolean(row["IsDeleted"])
            };

            list.Add(device);
        }

        return list;
    }
    //public static async Task UploadVehiclesWithOwnerNamesAsync(ANPR_Controller camera, string neighbourhood)
    //{
    //    string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";
    //    string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";

    //    string token = await GetTokenAsync(authUrl, clientId, clientSecret);
    //    if (string.IsNullOrEmpty(token))
    //    {
    //        LogApiRequest($"NamedUpload {camera.ANPRIP}: token fetch failed", logFilePath);
    //        Console.WriteLine("Token fetch failed.");
    //        return;
    //    }

    //    // Plate -> real Name mapping, sirf unhi plates ke liye jo master list mein already authorized hain
    //    var dataAccess = new ANPRDataAccess(connectionString);
    //    string sql = @"
    //    SELECT REPLACE(v.RegNo, ' ', '') AS Plate, ch.shortname AS Name
    //    FROM tblVehicle v
    //    INNER JOIN vwCardHolders ch ON ch.IDNumber = v.TagUID
    //    WHERE ch.Bld LIKE '%" + neighbourhood.Replace("'", "''") + @"%'";
    //    DataTable dt = dataAccess.GetDataTable(sql);

    //    var plateNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    //    if (dt != null)
    //    {
    //        foreach (DataRow row in dt.Rows)
    //        {
    //            string plate = row["Plate"].ToString().Trim().ToUpper();
    //            string name = row["Name"].ToString().Trim();
    //            if (!string.IsNullOrEmpty(plate) && !plateNameMap.ContainsKey(plate))
    //                plateNameMap[plate] = string.IsNullOrEmpty(name) ? "Resident" : name;
    //        }
    //    }

    //    if (plateNameMap.Count == 0)
    //    {
    //        LogApiRequest($"NamedUpload {camera.ANPRIP}: ABORTED - 0 plates with names for '{neighbourhood}'.", logFilePath);
    //        Console.WriteLine("No plate/name data found - aborting.");
    //        return;
    //    }

    //    Console.WriteLine($"{plateNameMap.Count} vehicle(s) with names found. Starting owner creation + upload...");

    //    var ownerIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    //    int uploadSuccess = 0, uploadFail = 0, ownerFail = 0;

    //    foreach (var kvp in plateNameMap)
    //    {
    //        string plate = kvp.Key;
    //        string name = kvp.Value;

    //        if (!ownerIdCache.TryGetValue(name, out string ownerId))
    //        {
    //            ownerId = await CreateOwnerAndGetIdAsync(baseUrl, token, name);
    //            if (string.IsNullOrEmpty(ownerId))
    //            {
    //                Console.WriteLine($"❌ Owner creation FAILED for '{name}' (plate {plate}) - skipping.");
    //                LogApiRequest($"NamedUpload {camera.ANPRIP}: owner creation failed for '{name}' (plate {plate})", logFilePath);
    //                ownerFail++;
    //                continue;
    //            }
    //            ownerIdCache[name] = ownerId;
    //            Console.WriteLine($"Owner ready: '{name}' -> id {ownerId}");
    //        }

    //        var client = new HttpClient();
    //        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle");
    //        request.Headers.Add("Authorization", "token " + token);
    //        request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
    //        request.Headers.Add("x-salt", "683239");
    //        request.Headers.Add("x-alpha", "21");

    //        var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": ""{name}"", ""owner"": ""{ownerId}""}}";
    //        request.Content = new StringContent(json, null, "application/json");

    //        var response = await client.SendAsync(request);
    //        string result = await response.Content.ReadAsStringAsync();

    //        if (response.IsSuccessStatusCode)
    //        {
    //            uploadSuccess++;
    //            Console.WriteLine($"✅ {plate} ({name}) uploaded.");
    //        }
    //        else
    //        {
    //            uploadFail++;
    //            Console.WriteLine($"❌ {plate} ({name}) FAILED: {result}");
    //        }
    //        LogApiRequest($"NamedUpload {camera.ANPRIP}: upload {plate} ({name}, owner={ownerId}) HTTP {(int)response.StatusCode} | {result}", logFilePath);
    //    }

    //    LogApiRequest($"NamedUpload {camera.ANPRIP}: DONE. Uploaded {uploadSuccess}/{plateNameMap.Count} (failed {uploadFail}, owner-fail {ownerFail}).", logFilePath);
    //    Console.WriteLine($"DONE. Uploaded {uploadSuccess}/{plateNameMap.Count} (failed {uploadFail}, owner-fail {ownerFail}).");
    //}

    //public static async Task<string> CreateOwnerAndGetIdAsync(string baseUrl, string token, string ownerName)
    //{
    //    string[] candidateEndpoints = { "owner", "owners" };

    //    foreach (var endpoint in candidateEndpoints)
    //    {
    //        try
    //        {
    //            var client = new HttpClient();
    //            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + endpoint);
    //            request.Headers.Add("Authorization", "token " + token);
    //            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
    //            request.Headers.Add("x-salt", "683239");
    //            request.Headers.Add("x-alpha", "21");

    //            string safeName = ownerName.Replace("\"", "\\\"");
    //            var json = $@"{{""name"": ""{safeName}""}}";
    //            request.Content = new StringContent(json, null, "application/json");

    //            var response = await client.SendAsync(request);
    //            string result = await response.Content.ReadAsStringAsync();
    //            LogApiRequest($"CreateOwner [{endpoint}] for '{ownerName}': HTTP {(int)response.StatusCode} | {result}", logFilePath);

    //            if (response.IsSuccessStatusCode)
    //            {
    //                string id = ExtractIdFromJson(result);
    //                if (!string.IsNullOrEmpty(id)) return id;
    //            }
    //            else if ((int)response.StatusCode == 404)
    //            {
    //                continue; // wrong endpoint name, try next
    //            }
    //            else
    //            {
    //                // "already exists" jaisi errors mein bhi kabhi kabhi existing id return hoti hai
    //                string idFromError = ExtractIdFromJson(result);
    //                if (!string.IsNullOrEmpty(idFromError)) return idFromError;
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            LogApiRequest($"CreateOwner [{endpoint}] exception for '{ownerName}': {ex.Message}", logFilePath);
    //        }
    //    }
    //    return null;
    //}



    //public static async Task UploadVehiclesWithOwnerNamesAsync(ANPR_Controller camera, string neighbourhood)
    //{
    //    string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";
    //    string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";

    //    string token = await GetTokenAsync(authUrl, clientId, clientSecret);
    //    if (string.IsNullOrEmpty(token))
    //    {
    //        LogApiRequest($"NamedUpload {camera.ANPRIP}: token fetch failed", logFilePath);
    //        Console.WriteLine("Token fetch failed.");
    //        return;
    //    }

    //    var dataAccess = new ANPRDataAccess(connectionString);
    //    string sql = @"
    //    SELECT REPLACE(v.RegNo, ' ', '') AS Plate, ch.shortname AS Name, ch.FlatNumber AS FlatNumber
    //    FROM tblVehicle v
    //    INNER JOIN vwCardHolders ch ON ch.IDNumber = v.TagUID
    //    WHERE ch.Bld LIKE '%" + neighbourhood.Replace("'", "''") + @"%'";
    //    DataTable dt = dataAccess.GetDataTable(sql);

    //    var plateInfoMap = new Dictionary<string, (string Name, string Flat)>(StringComparer.OrdinalIgnoreCase);
    //    if (dt != null)
    //    {
    //        foreach (DataRow row in dt.Rows)
    //        {
    //            string plate = row["Plate"].ToString().Trim().ToUpper();
    //            string name = row["Name"].ToString().Trim();
    //            string flat = row["FlatNumber"] == DBNull.Value ? "" : row["FlatNumber"].ToString().Trim();
    //            if (!string.IsNullOrEmpty(plate) && !plateInfoMap.ContainsKey(plate))
    //                plateInfoMap[plate] = (string.IsNullOrEmpty(name) ? "Resident" : name, flat);
    //        }
    //    }

    //    if (plateInfoMap.Count == 0)
    //    {
    //        LogApiRequest($"NamedUpload {camera.ANPRIP}: ABORTED - 0 plates with names for '{neighbourhood}'.", logFilePath);
    //        Console.WriteLine("No plate/name data found - aborting.");
    //        return;
    //    }

    //    Console.WriteLine($"{plateInfoMap.Count} vehicle(s) with names found. Starting owner creation + upload...");

    //    var ownerIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    //    int uploadSuccess = 0, uploadFail = 0, ownerFail = 0;

    //    foreach (var kvp in plateInfoMap)
    //    {
    //        string plate = kvp.Key;
    //        string name = kvp.Value.Name;
    //        string flat = kvp.Value.Flat;

    //        if (!ownerIdCache.TryGetValue(name, out string ownerId))
    //        {
    //            ownerId = await CreateOwnerAndGetIdAsync(baseUrl, token, name, flat);
    //            if (string.IsNullOrEmpty(ownerId))
    //            {
    //                Console.WriteLine($"❌ Owner creation FAILED for '{name}' (plate {plate}) - skipping.");
    //                ownerFail++;
    //                continue;
    //            }
    //            ownerIdCache[name] = ownerId;
    //            Console.WriteLine($"Owner ready: '{name}' -> id {ownerId}");
    //        }

    //        var client = new HttpClient();
    //        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle");
    //        request.Headers.Add("Authorization", "token " + token);
    //        request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
    //        request.Headers.Add("x-salt", "683239");
    //        request.Headers.Add("x-alpha", "21");

    //        var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": ""{name}"", ""owner"": ""{ownerId}""}}";
    //        request.Content = new StringContent(json, null, "application/json");

    //        var response = await client.SendAsync(request);
    //        string result = await response.Content.ReadAsStringAsync();

    //        if (response.IsSuccessStatusCode)
    //        {
    //            uploadSuccess++;
    //            Console.WriteLine($"✅ {plate} ({name}) uploaded.");
    //        }
    //        else
    //        {
    //            uploadFail++;
    //            Console.WriteLine($"❌ {plate} ({name}) FAILED: {result}");
    //        }
    //        LogApiRequest($"NamedUpload {camera.ANPRIP}: upload {plate} ({name}, owner={ownerId}) HTTP {(int)response.StatusCode} | {result}", logFilePath);
    //    }

    //    LogApiRequest($"NamedUpload {camera.ANPRIP}: DONE. Uploaded {uploadSuccess}/{plateInfoMap.Count} (failed {uploadFail}, owner-fail {ownerFail}).", logFilePath);
    //    Console.WriteLine($"DONE. Uploaded {uploadSuccess}/{plateInfoMap.Count} (failed {uploadFail}, owner-fail {ownerFail}).");
    //}

    //public static async Task<string> CreateOwnerAndGetIdAsync(string baseUrl, string token, string ownerName, string flatNumber)
    //{
    //    try
    //    {
    //        var client = new HttpClient();
    //        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicleowner");
    //        request.Headers.Add("Authorization", "token " + token);
    //        request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
    //        request.Headers.Add("x-salt", "683239");
    //        request.Headers.Add("x-alpha", "21");

    //        string safeName = ownerName.Replace("\"", "\\\"");
    //        string safeFlat = string.IsNullOrEmpty(flatNumber) ? "NA" : flatNumber.Replace("\"", "\\\"");

    //        //    var json = $@"{{
    //        //    ""name"": ""{safeName}"",
    //        //    ""phone_number"": ""0000000000"",
    //        //    ""owner_type"": 1,
    //        //    ""flat_number"": ""{safeFlat}"",
    //        //    ""is_custom_flat_number"": true
    //        //}}";
    //        var json = $@"{{
    //        ""name"": ""{safeName}"",
    //        ""phone_number"": ""9999999999"",
    //        ""owner_type"": 1,
    //        ""flat_number"": ""{safeFlat}"",
    //        ""is_custom_flat_number"": true
    //    }}";
    //        request.Content = new StringContent(json, null, "application/json");

    //        var response = await client.SendAsync(request);
    //        string result = await response.Content.ReadAsStringAsync();
    //        LogApiRequest($"CreateOwner [vehicleowner] for '{ownerName}': HTTP {(int)response.StatusCode} | {result}", logFilePath);

    //        if (response.IsSuccessStatusCode)
    //        {
    //            string id = ExtractIdFromJson(result);
    //            if (!string.IsNullOrEmpty(id)) return id;
    //        }
    //        else
    //        {
    //            string idFromError = ExtractIdFromJson(result);
    //            if (!string.IsNullOrEmpty(idFromError)) return idFromError;
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        LogApiRequest($"CreateOwner [vehicleowner] exception for '{ownerName}': {ex.Message}", logFilePath);
    //    }
    //    return null;
    //}



    public static async Task UploadVehiclesWithOwnerNamesAsync(ANPR_Controller camera, string neighbourhood)
    {
        string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";
        string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";

        string token = await GetTokenAsync(authUrl, clientId, clientSecret);
        if (string.IsNullOrEmpty(token))
        {
            LogApiRequest($"NamedUpload {camera.ANPRIP}: token fetch failed", logFilePath);
            Console.WriteLine("Token fetch failed.");
            return;
        }

        var dataAccess = new ANPRDataAccess(connectionString);

        // Single source now - name, flat, owner type all come from the view directly
        //string masterSql = @"
        //SELECT number_plate, vehicle_type, FullName, FlatNumber, OwnerType
        //FROM ANPR_MasterVehicleList
        //WHERE Neighbourhood = '" + neighbourhood.Replace("'", "''") + @"'";

        //        string masterSql = @"
        //SELECT number_plate, vehicle_type, FullName, FlatNumber, OwnerType, AgreementFrom, AgreementTo
        //FROM ANPR_MasterVehicleList
        //WHERE Neighbourhood = '" + neighbourhood.Replace("'", "''") + @"'";
        //        DataTable masterDt = dataAccess.GetDataTable(masterSql);

        string masterSql = @"
SELECT number_plate, vehicle_type, FullName, FlatNumber, OwnerType, AgreementFrom, AgreementTo
FROM ANPR_MasterVehicleList
WHERE Neighbourhood = '" + neighbourhood.Replace("'", "''") + @"'
  AND IsUploadedToANPR = 'N'";
        DataTable masterDt = dataAccess.GetDataTable(masterSql);

        if (masterDt == null || masterDt.Rows.Count == 0)
        {
            LogApiRequest($"NamedUpload {camera.ANPRIP}: ABORTED - 0 rows in master list for '{neighbourhood}'.", logFilePath);
            return;
        }

        //Console.WriteLine($"{masterDt.Rows.Count} vehicle(s) in master list. Starting owner creation + upload...");

        //var ownerIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // key = name+flat
        //int uploadSuccess = 0, uploadFail = 0, ownerFail = 0;
        //long phoneSeq = 7000000000; // synthetic unique phone base - avoids owner collision



        Console.WriteLine($"{masterDt.Rows.Count} vehicle(s) in master list. Starting owner creation + upload...");

        // Seed with owners ALREADY on the camera so we never re-create the same person - this was the bug.
        var ownerIdCache = await GetExistingOwnersAsync(baseUrl, token); // key = name+flat
        Console.WriteLine($"{ownerIdCache.Count} existing owner(s) found on camera - reusing them.");
        int uploadSuccess = 0, uploadFail = 0, ownerFail = 0;
        long phoneSeq = 7000000000; // synthetic unique phone base - avoids owner collision

        foreach (DataRow row in masterDt.Rows)
        {
            string plate = row["number_plate"]?.ToString().Trim().ToUpper();
            if (string.IsNullOrEmpty(plate)) continue;

            int vType = row["vehicle_type"] != DBNull.Value ? Convert.ToInt32(row["vehicle_type"]) : 0;
            string name = row["FullName"] == DBNull.Value ? "" : row["FullName"].ToString().Trim();
            if (string.IsNullOrEmpty(name)) name = "Resident";
            string flat = row["FlatNumber"] == DBNull.Value ? "" : row["FlatNumber"].ToString().Trim();
            if (string.IsNullOrEmpty(flat)) flat = "NA";
            string ownerType = row["OwnerType"] == DBNull.Value ? "Member" : row["OwnerType"].ToString().Trim();

            DateTime? agreementFrom = row["AgreementFrom"] != DBNull.Value ? Convert.ToDateTime(row["AgreementFrom"]) : (DateTime?)null;
            DateTime? agreementTo = row["AgreementTo"] != DBNull.Value ? Convert.ToDateTime(row["AgreementTo"]) : (DateTime?)null;

            string ownerKey = name + "|" + flat; // separate owners even if two flats share a name
            if (!ownerIdCache.TryGetValue(ownerKey, out string ownerId))
            {
                phoneSeq++;
                ownerId = await CreateOwnerAndGetIdAsync(baseUrl, token, name, flat, phoneSeq.ToString(), ownerType, agreementFrom, agreementTo);
                if (string.IsNullOrEmpty(ownerId))
                {
                    Console.WriteLine($"❌ Owner creation FAILED for '{name}' (plate {plate}) - skipping.");
                    ownerFail++;
                    continue;
                }
                ownerIdCache[ownerKey] = ownerId;
                Console.WriteLine($"Owner ready: '{name}' ({flat}, {ownerType}) -> id {ownerId}");
            }

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle");
            request.Headers.Add("Authorization", "token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");

            var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": {vType}, ""auth_type"": ""whitelist"", ""comment"": ""{name}"", ""owner"": ""{ownerId}""}}";
            request.Content = new StringContent(json, null, "application/json");

            //var response = await client.SendAsync(request);
            //string result = await response.Content.ReadAsStringAsync();

            //if (response.IsSuccessStatusCode) uploadSuccess++;
            //else uploadFail++;

            //LogApiRequest($"NamedUpload {camera.ANPRIP}: upload {plate} ({name}, owner={ownerId}) HTTP {(int)response.StatusCode} | {result}", logFilePath);


            var response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                uploadSuccess++;
                dataAccess.GetDataTable(
                    "UPDATE tblVehicle SET IsUploadedToANPR = 'Y' WHERE REPLACE(RegNo, ' ', '') = '"
                    + plate.Replace("'", "''") + "'");
            }
            else uploadFail++;

            LogApiRequest($"NamedUpload {camera.ANPRIP}: upload {plate} ({name}, owner={ownerId}) HTTP {(int)response.StatusCode} | {result}", logFilePath);
        }

        LogApiRequest($"NamedUpload {camera.ANPRIP}: DONE. Uploaded {uploadSuccess}/{masterDt.Rows.Count} (failed {uploadFail}, owner-fail {ownerFail}).", logFilePath);
        Console.WriteLine($"DONE. Uploaded {uploadSuccess}/{masterDt.Rows.Count} (failed {uploadFail}, owner-fail {ownerFail}).");
    }



    public static async Task<Dictionary<string, string>> GetExistingOwnersAsync(string baseUrl, string token)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // key = name|flat
        string url = baseUrl + "vehicleowner";

        while (!string.IsNullOrEmpty(url))
        {
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");

            HttpResponseMessage response;
            try { response = await httpClient.SendAsync(request); }
            catch (Exception netEx)
            {
                LogApiRequest($"GetExistingOwners: network error - {netEx.Message}", logFilePath);
                break;
            }
            if (!response.IsSuccessStatusCode)
            {
                LogApiRequest($"GetExistingOwners: HTTP {(int)response.StatusCode}", logFilePath);
                break;
            }

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("results", out JsonElement results))
            {
                foreach (JsonElement item in results.EnumerateArray())
                {
                    string id = item.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
                    string name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() : "";
                    string flat = item.TryGetProperty("flat_number", out var fEl) ? fEl.GetString() : "";
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

                    string key = name.Trim() + "|" + (string.IsNullOrEmpty(flat) ? "NA" : flat.Trim());
                    owners[key] = id; // if dupes already exist on camera, last one found wins - fine
                }
            }

            url = (root.TryGetProperty("next", out JsonElement nextEl) && nextEl.ValueKind == JsonValueKind.String)
                ? nextEl.GetString() : null;
        }

        LogApiRequest($"GetExistingOwners {baseUrl}: {owners.Count} unique owner(s) loaded.", logFilePath);
        return owners;
    }

    public static async Task CleanupOrphanedOwnersAsync(ANPR_Controller camera)
    {
        string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";
        string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";
        string token = await GetTokenAsync(authUrl, clientId, clientSecret);
        if (string.IsNullOrEmpty(token)) { Console.WriteLine("Token fetch failed."); return; }

        // Step 1: collect owner IDs actually referenced by a vehicle right now
        var usedOwnerIds = new HashSet<string>();
        string vurl = baseUrl + "vehicle";
        while (!string.IsNullOrEmpty(vurl))
        {
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, vurl);
            request.Headers.Add("Authorization", "Token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            HttpResponseMessage response;
            try { response = await httpClient.SendAsync(request); } catch { break; }
            if (!response.IsSuccessStatusCode) break;
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("results", out JsonElement results))
                foreach (JsonElement item in results.EnumerateArray())
                    if (item.TryGetProperty("owner", out var ownEl) && ownEl.ValueKind != JsonValueKind.Null)
                        usedOwnerIds.Add(ownEl.ToString());
            vurl = (root.TryGetProperty("next", out JsonElement n1) && n1.ValueKind == JsonValueKind.String) ? n1.GetString() : null;
        }
        Console.WriteLine($"{camera.ANPRIP}: {usedOwnerIds.Count} owner id(s) actually in use by current vehicles.");
        LogApiRequest($"CleanupOwners {camera.ANPRIP}: {usedOwnerIds.Count} owner id(s) in use.", logFilePath);

        // Step 2: walk every owner, mark ones NOT referenced by any vehicle as orphaned
        var toDelete = new List<string>();
        string ourl = baseUrl + "vehicleowner";
        while (!string.IsNullOrEmpty(ourl))
        {
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, ourl);
            request.Headers.Add("Authorization", "Token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            HttpResponseMessage response;
            try { response = await httpClient.SendAsync(request); } catch { break; }
            if (!response.IsSuccessStatusCode) break;
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("results", out JsonElement results))
                foreach (JsonElement item in results.EnumerateArray())
                {
                    string id = item.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
                    if (!string.IsNullOrEmpty(id) && !usedOwnerIds.Contains(id))
                        toDelete.Add(id);
                }
            ourl = (root.TryGetProperty("next", out JsonElement n2) && n2.ValueKind == JsonValueKind.String) ? n2.GetString() : null;
        }
        Console.WriteLine($"{camera.ANPRIP}: {toDelete.Count} orphaned owner(s) found. Deleting...");
        LogApiRequest($"CleanupOwners {camera.ANPRIP}: {toDelete.Count} orphaned owner(s) to delete.", logFilePath);

        int deleted = 0, failed = 0;
        foreach (var id in toDelete)
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Delete, baseUrl + "vehicleowner/" + id);
            request.Headers.Add("Authorization", "token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            HttpResponseMessage response;
            try { response = await client.SendAsync(request); } catch { failed++; continue; }
            if (response.IsSuccessStatusCode) deleted++; else failed++;
        }
        Console.WriteLine($"{camera.ANPRIP}: CLEANUP DONE. Deleted {deleted}, failed {failed}.");
        LogApiRequest($"CleanupOwners {camera.ANPRIP}: DONE. Deleted={deleted}, Failed={failed}.", logFilePath);
    }



    //public static async Task<string> CreateOwnerAndGetIdAsync(string baseUrl, string token, string ownerName, string flatNumber, string phoneNumber, string ownerType)
    //{
    //    try
    //    {
    //        var client = new HttpClient();
    //        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicleowner");
    //        request.Headers.Add("Authorization", "token " + token);
    //        request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
    //        request.Headers.Add("x-salt", "683239");
    //        request.Headers.Add("x-alpha", "21");

    //        string safeName = ownerName.Replace("\"", "\\\"");
    //        string safeFlat = string.IsNullOrEmpty(flatNumber) ? "NA" : flatNumber.Replace("\"", "\\\"");
    //        int ownerTypeCode = ownerType == "Tenant" ? 2 : 1; // TODO: confirm actual API codes for Member vs Tenant

    //        var json = $@"{{
    //        ""name"": ""{safeName}"",
    //        ""phone_number"": ""{phoneNumber}"",
    //        ""owner_type"": {ownerTypeCode},
    //        ""flat_number"": ""{safeFlat}"",
    //        ""is_custom_flat_number"": true
    //    }}";
    //        request.Content = new StringContent(json, null, "application/json");

    //        var response = await client.SendAsync(request);
    //        string result = await response.Content.ReadAsStringAsync();
    //        LogApiRequest($"CreateOwner [vehicleowner] for '{ownerName}': HTTP {(int)response.StatusCode} | {result}", logFilePath);

    //        if (response.IsSuccessStatusCode)
    //        {
    //            string id = ExtractIdFromJson(result);
    //            if (!string.IsNullOrEmpty(id)) return id;
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        LogApiRequest($"CreateOwner [vehicleowner] exception for '{ownerName}': {ex.Message}", logFilePath);
    //    }
    //    return null;
    //}


    public static async Task<string> CreateOwnerAndGetIdAsync(string baseUrl, string token, string ownerName, string flatNumber, string phoneNumber, string ownerType, DateTime? agreementFrom, DateTime? agreementTo)
    {
        try
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicleowner");
            request.Headers.Add("Authorization", "token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");

            string safeName = ownerName.Replace("\"", "\\\"");
            string safeFlat = string.IsNullOrEmpty(flatNumber) ? "NA" : flatNumber.Replace("\"", "\\\"");
            int ownerTypeCode = ownerType == "Tenant" ? 2 : 1;

            string extraFields = "";
            if (ownerTypeCode == 2) // Tenant - camera API requires validity dates
            {
                // Real agreement dates from DB - fallback to today->+1yr only if DB has no dates
                DateTime start = agreementFrom ?? DateTime.Now;
                DateTime end = agreementTo ?? DateTime.Now.AddYears(1);
                extraFields = $@",""validity_start_date"": ""{start:yyyy-MM-dd}"", ""validity_end_date"": ""{end:yyyy-MM-dd}""";
            }

            var json = $@"{{
        ""name"": ""{safeName}"",
        ""phone_number"": ""{phoneNumber}"",
        ""owner_type"": {ownerTypeCode},
        ""flat_number"": ""{safeFlat}"",
        ""is_custom_flat_number"": true{extraFields}
    }}";
            request.Content = new StringContent(json, null, "application/json");

            var response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            LogApiRequest($"CreateOwner [vehicleowner] for '{ownerName}': HTTP {(int)response.StatusCode} | {result}", logFilePath);

            if (response.IsSuccessStatusCode)
            {
                string id = ExtractIdFromJson(result);
                if (!string.IsNullOrEmpty(id)) return id;
            }
            else
            {
                Console.WriteLine($"⚠ CreateOwner HTTP {(int)response.StatusCode} for '{ownerName}': {result}");
                string idFromError = ExtractIdFromJson(result);
                if (!string.IsNullOrEmpty(idFromError)) return idFromError;
            }
        }
        catch (Exception ex)
        {
            LogApiRequest($"CreateOwner [vehicleowner] exception for '{ownerName}': {ex.Message}", logFilePath);
            Console.WriteLine($"⚠ CreateOwner exception for '{ownerName}': {ex.Message}");
        }
        return null;
    }
    public static string ExtractIdFromJson(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return FindIdInElement(doc.RootElement);
        }
        catch { return null; }
    }

    private static string FindIdInElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase)
                    && (prop.Value.ValueKind == JsonValueKind.Number || prop.Value.ValueKind == JsonValueKind.String))
                    return prop.Value.ToString();
            }
            foreach (var prop in element.EnumerateObject())
            {
                var found = FindIdInElement(prop.Value);
                if (!string.IsNullOrEmpty(found)) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindIdInElement(item);
                if (!string.IsNullOrEmpty(found)) return found;
            }
        }
        return null;
    }
    static async Task ScheduleTask(string name, TimeSpan interval)
    {
        List<ANPR_Controller> myCameras = await GetCameraAsync();
        string _name = "CallGetApiAsync";
        LogApiRequest(@"Start fetchin Active Cameras...", logFilePath);

        while (true)
        {
            if (_name == "CallGetApiAsync")
            { _name = "CallPostApiListAsync"; }
            else if (_name == "CallPostApiListAsync")
            { _name = "GetVehicleEventListAsync"; }
            else if (_name == "GetVehicleEventListAsync")
            { _name = "GetVehicleEventImgListAsync"; }
            else if (_name == "GetVehicleEventImgListAsync")
            { _name = "DeleteBulkVehicleListAsync"; }
            else if (_name == "DeleteBulkVehicleListAsync")
            { _name = "CallGetApiAsync"; }

            foreach (var camera in myCameras)
            {
                Console.WriteLine(DateTime.Now.ToString("dd-mm-yyyy HH:mm") + ": " + $"CameraIP: {camera.ANPRIP}, TerminalID: {camera.ANPRTerminalID}, Method Name:{_name} ");
                LogApiRequest($"CameraIP: {camera.ANPRIP}, TerminalID: {camera.ANPRTerminalID}, Method Name:{_name} ", logFilePath);
                ANPRDataAccess CameraStatusUpdate = new ANPRDataAccess(connectionString);

                string baseUrlIP = camera.ANPRIP; string authIP = camera.ANPRIP;
                baseUrl = "http://" + baseUrlIP + "/api/v1/sdk/";
                authUrl = "http://" + authIP + "/api/v1/sdk/login";

                try
                {
                    TimeSpan diff;
                    bool isOnline = IsPingSuccessful(camera.ANPRIP);
                    if (isOnline)
                    {
                        await SyncCameraEventsNowAsync(camera, baseUrl, authUrl);
                    }
                    int trImagesSyncDiff = 0;

                    if (isOnline && _name == "CallGetApiAsync")
                    {
                        if (camera.last_updatedOn == null)
                            camera.last_updatedOn = DateTime.Now.AddHours(-4);

                        diff = Convert.ToDateTime(DateTime.Now) - Convert.ToDateTime(camera.last_updatedOn);

                        if (diff.TotalMinutes >= 20)
                        {
                            try
                            {
                                Console.WriteLine("Fetching token... " + authUrl);
                                token = await GetTokenAsync(authUrl, clientId, clientSecret);

                                string Token = ""; Token = token;
                                if (!string.IsNullOrEmpty(Token))
                                {
                                    await CallGetApiAsync(baseUrl + "vehicle", Token, camera.ANPRTerminalID.ToString());
                                    Console.WriteLine($"{baseUrl} method  {_name} executed successfully at {DateTime.Now.ToString()}");
                                    LogApiRequest($"{baseUrl} method  {_name} executed successfully at {DateTime.Now.ToString()}", logFilePath);
                                }
                                else
                                {
                                    Console.WriteLine("Fetching token failed... " + authUrl);
                                }
                            }
                            catch (Exception Err)
                            {
                                Console.WriteLine("Error :{authUrl} " + Err.Message.ToString());
                            }
                            finally
                            {
                                Console.WriteLine("Move Next API");
                            }
                        }
                    }
                    else if (isOnline && _name == "GetVehicleAsync")
                    {
                    }
                    else if (isOnline && _name == "CallPostApiListAsync")
                    {
                        try
                        {
                            if (camera.last_updatedOn == null)
                                camera.last_updatedOn = DateTime.Now.AddHours(-2);
                            diff = Convert.ToDateTime(DateTime.Now) - Convert.ToDateTime(camera.last_updatedOn);

                            //if (diff.TotalMinutes > 25)
                            //{
                            //    await CallPostApiListAsync(baseUrl + "vehicle", "", camera);
                            //    Console.WriteLine($"{_name} executed at {DateTime.Now}");
                            //    LogApiRequest($"{baseUrl} method  {_name} executed successfully at {DateTime.Now}", logFilePath);

                            //    bool syncUpload = true;
                            //    var dataAccess = new ANPRDataAccess(connectionString);
                            //    await dataAccess.InsertOrUpdateCameraStatusSync(camera, syncUpload);
                            //}

                            if (diff.TotalMinutes > 25)
                            {
                                await UploadVehiclesWithOwnerNamesAsync(camera, camera.ANPRLocation);
                                Console.WriteLine($"{_name} executed at {DateTime.Now}");
                                LogApiRequest($"{baseUrl} method  {_name} executed successfully at {DateTime.Now}", logFilePath);

                                bool syncUpload = true;
                                var dataAccess = new ANPRDataAccess(connectionString);
                                await dataAccess.InsertOrUpdateCameraStatusSync(camera, syncUpload);
                            }
                        }
                        catch (Exception Err)
                        {
                            Console.WriteLine($"Error :{authUrl} " + Err.Message.ToString());
                            LogApiRequest($"Error :{authUrl} " + Err.Message.ToString(), logFilePath);
                        }
                    }
                    //else if (isOnline && _name == "GetVehicleEventImgListAsync")
                    //{
                    //    try
                    //    {
                    //        Console.WriteLine("Fetching token... " + authUrl);

                    //        token = await GetTokenAsync(authUrl, clientId, clientSecret);
                    //        string Token = ""; Token = token;
                    //        if (Token != null)
                    //        {
                    //            await Task_GetVehicleEventImagesAsync(camera);
                    //            Console.WriteLine($"{_name} image sync completed at {DateTime.Now}");
                    //            LogApiRequest($"{_name} image sync completed for camera {camera.ANPRIP} at {DateTime.Now}", logFilePath);
                    //        }
                    //    }
                    //    catch (Exception Err)
                    //    {
                    //        Console.WriteLine("Error :{authUrl} " + Err.Message.ToString());
                    //    }
                    //}



                    //else if (isOnline && _name == "GetVehicleEventImgListAsync")
                    //{
                    //    try
                    //    {
                    //        var (done, failed, processed) = await ImageFetchWorker.ProcessOneCameraBatchAsync(camera, connectionString, clientId, clientSecret, 500);
                    //        Console.WriteLine($"{_name} image sync: {processed} processed ({done} done, {failed} failed) at {DateTime.Now}");
                    //        LogApiRequest($"{_name} image sync for camera {camera.ANPRIP}: {processed} processed ({done} done, {failed} failed) at {DateTime.Now}", logFilePath);
                    //    }
                    //    catch (Exception Err)
                    //    {
                    //        Console.WriteLine("Error :{authUrl} " + Err.Message.ToString());
                    //    }
                    //}


                    else if (isOnline && _name == "GetVehicleEventImgListAsync")
                    {
                        // Image-fetch ab dedicated background loops (TodaySyncLoopAsync + BacklogDrainLoopAsync)
                        // handle karte hain, Main() mein already start ho chuke - yahan kuch nahi karna, duplicate avoid karne ke liye
                    }

                    else if (isOnline && _name == "DeleteBulkVehicleListAsync")
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(camera.ANPRLocation))
                            {
                                await ReconcileCameraToMasterListAsync(camera, camera.ANPRLocation);
                            }
                        }
                        catch (Exception Err)
                        {
                            Console.WriteLine($"Error :{authUrl} " + Err.Message.ToString());
                            LogApiRequest($"Error :{authUrl} " + Err.Message.ToString(), logFilePath);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{_name} executed at {DateTime.Now}, Camera Status connected : " + isOnline.ToString());
                    }

                    Console.WriteLine($"All Camera Execution completed for - {_name} @ {DateTime.Now}");
                }
                catch (Exception Err)
                {
                    Console.WriteLine("Error :{authUrl} " + Err.Message.ToString());
                }
                finally
                {
                    Console.WriteLine("Move Next API");
                }
            }
            Thread.Sleep(100000);
        }
    }
    public static async Task SyncCameraEventsNowAsync(ANPR_Controller camera, string baseUrl, string authUrl)
    {
        try
        {
            if (camera.last_downloadedOn == null)
                camera.last_downloadedOn = DateTime.Now.AddHours(-4);
            //old code 22 sep 2026 untill
            //string dwnDt = Convert.ToDateTime(camera.last_downloadedOn).ToString("yyyy-MM-dd HH:mm:ss");
            //string evToken = await GetTokenAsync(authUrl, clientId, clientSecret);

            //new code 22 sep 2026
            string dwnDt = Convert.ToDateTime(camera.last_downloadedOn).ToString("yyyy-MM-dd HH:mm:ss");
            string dwnDtEncoded = Uri.EscapeDataString(dwnDt); // FIX: space/colon URL-encode karo, warna Skylark cameras (.8/.10/.12) silently 0 results dete hain
            string evToken = await GetTokenAsync(authUrl, clientId, clientSecret);
            if (string.IsNullOrEmpty(evToken))
            {
                LogApiRequest($"Camera {camera.ANPRIP}: token fetch failed, retry next loop.", logFilePath);
                return;
            }
            // Old code 22 sep 2026 untill
            //string url = baseUrl + "events?timestamp__gte=" + dwnDt;

            //new code 22 sep 2026
            string url = baseUrl + "events?timestamp__gte=" + dwnDtEncoded;
            DateTime newest = DateTime.MinValue;
            int inserted = 0, pageCount = 0;
            var dataAccess = new ANPRDataAccess(connectionString);

            while (!string.IsNullOrEmpty(url))
            {
                //pageCount++;
                //if (pageCount > 300)
                //{
                //    LogApiRequest($"Camera {camera.ANPRIP}: page cap hit, resuming next loop.", logFilePath);
                //    return; // watermark NOT advanced - retries same window automatically
                //}

                pageCount++;
                if (pageCount > 300)
                {
                    // Advance watermark to what we've actually processed so far -
                    // guarantees forward progress even on a huge backlog, instead of
                    // re-reading the same stuck window forever.
                    DateTime partialWatermark = newest > DateTime.MinValue ? newest : Convert.ToDateTime(camera.last_downloadedOn);
                    await dataAccess.InsertOrUpdateCameraStatusSync(camera, false, partialWatermark);
                    camera.last_downloadedOn = partialWatermark;
                    LogApiRequest($"Camera {camera.ANPRIP}: page cap hit at page {pageCount}, watermark advanced to {partialWatermark:yyyy-MM-dd HH:mm:ss} (partial progress, continuing next loop).", logFilePath);
                    return;
                }
                var httpClient = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Token " + evToken);
                request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                request.Headers.Add("x-salt", "683239");
                request.Headers.Add("x-alpha", "21");

                var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    LogApiRequest($"Camera {camera.ANPRIP}: page {pageCount} HTTP {response.StatusCode} - watermark NOT advanced.", logFilePath);
                    return;
                }

                string jsonString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonString);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("results", out JsonElement results))
                {
                    foreach (JsonElement item in results.EnumerateArray())
                    {
                        try
                        {
                            //odl code 24 sep 2026 untill just added in new vehicle type

                            //int _id = item.GetProperty("id").GetInt32();
                            //string _numberPlate = item.GetProperty("number_plate").GetString();
                            //string _unique_id = item.GetProperty("unique_id").ToString();
                            //DateTime _inOutTime = DateTime.Parse(
                            //    item.GetProperty("updated_at").ToString(),
                            //    System.Globalization.CultureInfo.InvariantCulture,
                            //    System.Globalization.DateTimeStyles.RoundtripKind);

                            //if (_inOutTime > newest) newest = _inOutTime;

                            //var InOutLog = new ANPR_VehicleInoutRegister_log
                            //{
                            //    id = _id,
                            //    unique_id = _unique_id,
                            //    CameraID = camera.ID,
                            //    CameraLocation = camera.ANPRLocation,
                            //    VehicleNumber = _numberPlate ?? "",
                            //    OwnerID = null,
                            //    InOutFlag = camera.ANPR_Mode,
                            //    InOutTime = _inOutTime
                            //};

                            int _id = item.GetProperty("id").GetInt32();
                            string _numberPlate = item.GetProperty("number_plate").GetString();
                            string _unique_id = item.GetProperty("unique_id").ToString();
                            DateTime _inOutTime = DateTime.Parse(
                                item.GetProperty("updated_at").ToString(),
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind);

                            int? _vehicleType = null;
                            if (item.TryGetProperty("vehicle_type", out JsonElement vtEl) && vtEl.ValueKind == JsonValueKind.Number)
                            {
                                _vehicleType = vtEl.GetInt32();
                            }

                            if (_inOutTime > newest) newest = _inOutTime;

                            var InOutLog = new ANPR_VehicleInoutRegister_log
                            {
                                id = _id,
                                unique_id = _unique_id,
                                CameraID = camera.ID,
                                CameraLocation = camera.ANPRLocation,
                                VehicleNumber = _numberPlate ?? "",
                                OwnerID = null,
                                InOutFlag = camera.ANPR_Mode,
                                InOutTime = _inOutTime,
                                VehicleType = _vehicleType
                            };

                            await dataAccess.InsertOrUpdateANPRVehicleEventsAsync(InOutLog);
                            inserted++;
                        }
                        catch (Exception itemEx)
                        {
                            LogApiRequest($"Camera {camera.ANPRIP}: bad event item skipped: {itemEx.Message}", logFilePath);
                        }
                    }
                }

                url = (root.TryGetProperty("next", out JsonElement nextEl) && nextEl.ValueKind == JsonValueKind.String)
                    ? nextEl.GetString() : null;
            }

            //DateTime newWatermark = newest > DateTime.MinValue ? newest : DateTime.Now;
            //await dataAccess.InsertOrUpdateCameraStatusSync(camera, false, newWatermark);
            //camera.last_downloadedOn = newWatermark; // keep in-memory list fresh too

            //LogApiRequest($"Camera {camera.ANPRIP}: synced {inserted} event(s), {pageCount} page(s). Watermark -> {newWatermark:yyyy-MM-dd HH:mm:ss}", logFilePath);
            // FIX 22 sep 2026: purane code mein 0 events milne par bhi watermark = DateTime.Now
            // set ho jaata tha, jisse thodi der pehle bane events permanently skip ho jaate the
            // (Skylark .8/.10/.12 par continuous 0-sync loop, 3 din data miss).
            // Naya rule: watermark sirf tabhi aage badhao jab REAL events mile hoon.
            // 0 events => watermark untouched, agla poll wahi window dobara check karega.
            // Duplicate ka risk nahi - InsertOrUpdateANPRVehicleEventsAsync already
            // CameraID+unique_id check karke skip karta hai agar record exist karta hai.
            if (newest > DateTime.MinValue)
            {
                await dataAccess.InsertOrUpdateCameraStatusSync(camera, false, newest);
                camera.last_downloadedOn = newest; // keep in-memory list fresh too

                LogApiRequest($"Camera {camera.ANPRIP}: synced {inserted} event(s), {pageCount} page(s). Watermark -> {newest:yyyy-MM-dd HH:mm:ss}", logFilePath);
            }
            else
            {
                LogApiRequest($"Camera {camera.ANPRIP}: 0 event(s) this cycle, {pageCount} page(s). Watermark UNCHANGED at {camera.last_downloadedOn:yyyy-MM-dd HH:mm:ss}.", logFilePath);
            }
        }
        catch (Exception ex)
        {
            LogApiRequest($"Camera {camera.ANPRIP}: SyncCameraEventsNowAsync error: {ex.Message} - watermark NOT advanced.", logFilePath);
        }
    }
    public static async Task Task_GetVehicleEventImagesAsync(ANPR_Controller Camera)
    {
        try
        {

            //vwGetVehicleAccessImageUpdateList
            // ANPR_Controller Terminal = camera;//.ANPRTerminalID.ToString();
            string baseUrlIP = Camera.ANPRIP; string authIP = Camera.ANPRIP;
            string baseUrl = "http://" + baseUrlIP + "/api/v1/sdk/";
            string authUrl = "http://" + authIP + "/api/v1/sdk/login";

            List<vehicleAccessImageUpdateList> vehlst = new List<vehicleAccessImageUpdateList>();
            vehlst = await GetVehicleAccessImageUpdateList(Camera.ANPRTerminalID);
            if (vehlst.Count > 0)
            {
                baseUrlIP = Camera.ANPRIP; authIP = Camera.ANPRIP;
                baseUrl = "http://" + baseUrlIP + "/api/v1/sdk/";
                authUrl = "http://" + authIP + "/api/v1/sdk/login";
                List<VehicleEvent> evList = new List<VehicleEvent>();
                foreach (var ve in vehlst)
                {
                    string messageid = ve.unique_id;

                    try
                    {
                        VehicleEvent ev = new VehicleEvent();
                        ev.ImageUrl = baseUrl + "event-image";

                        ev.authUrl = authUrl;
                        ev.UniqueId = messageid;
                        ev.PlateNumber = ve.VehicleNumber;
                        ev.CameraId = Camera.ANPRTerminalID;
                        evList.Add(ev);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error in Image fetch : {messageid} - " + ex.Message.ToString());
                        Program.LogApiRequest($"Error in Image fetch : {Camera.ANPRTerminalID + "-" + messageid} " + ex.Message.ToString(), Program.logFilePath);
                    }

                }

                //Save Image on path
                foreach (var ev in evList)
                {
                    try
                    {
                        ////                            string Token = ""; Token = token;
                        //var r = Program.CallGetVehicleEventImageList(ev);
                        //Console.WriteLine($"{ev.ImageUrl} get image method for {ev.PlateNumber} execution completed at {DateTime.Now}");

                        //                            string Token = ""; Token = token;
                        var r = await Program.CallGetVehicleEventImageList(ev);
                        Console.WriteLine($"{ev.ImageUrl} get image method for {ev.PlateNumber} execution completed at {DateTime.Now}");

                    }
                    catch (Exception ex)
                    {
                        Program.LogApiRequest($"Error : Exception {ev.PlateNumber}" + ex.Message.ToString(), Program.logFilePath);
                        Console.WriteLine($"Error : Exception {ev.PlateNumber}" + ex.Message.ToString());
                    }
                }
            }
            else
            {
                Console.WriteLine($"No vehicle uniqueid to fetch image at {DateTime.Now}");
            }
        }
        catch (Exception Err)
        {
            Console.WriteLine($"Error :{"http://" + Camera.ANPRIP + "/api/v1/sdk/event-image.."} " + Err.Message.ToString());
            // Log an API request
            //Program.LogApiRequest($"Error :{"http://" + Camera.ANPRIP + "/api/v1/sdk/event-image.."} " + Err.Message.ToString(), Program.logFilePath);
        }
        finally
        {
            Console.WriteLine("Move Next API");
        }
    }
    public static void LogApiRequest(string apiUrl, string logFilePath)
    {
        if (logFilePath != "" && !Directory.Exists(Path.GetDirectoryName(logFilePath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
        }

        // Define the log file path (you can change this to a path on your server)
        logFilePath = Directory.GetCurrentDirectory().ToString() + @"\Logs\ApiLog_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
        //logFilePath = Directory.GetCurrentDirectory().ToString() + @"\Logs\ApiLog {DateTime.Now:yyyy-MM-dd}.txt";         
        string logMessage = $"[Request] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Calling API: {apiUrl}";
        WriteToLog(logMessage, logFilePath);
    }
    public static void WriteToLog(string logMessage, string logFilePath)
    {
        try
        {
            using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
            {
                writer.WriteLine(logMessage);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing to log file: {ex.Message}");
        }
    }


    public static async Task<string> CallGetVehicleEventImageList(VehicleEvent ve)
    {
        //ve.ImageUrl = "http://localhost:5118/media/plates/Plate1010.jpeg?md5=1234&expires=10000000";
        if (1 == 1)
        {
            //http://{{host}}/api/v1/sdk/vehicle
            try
            {
                string jsonString = "";
                if (1 == 1)
                {

                    //var client = new HttpClient();
                    //var request = new HttpRequestMessage(HttpMethod.Post, ve.ImageUrl); 

                    //Console.WriteLine("Fetching token... " + authUrl);
                    string token = await Program.GetTokenAsync(ve.authUrl, Program.clientId, Program.clientSecret);
                    if (token != null)
                    {
                        string uniqueID = ve.UniqueId;
                        var client = new HttpClient();
                        var request = new HttpRequestMessage(HttpMethod.Post, ve.ImageUrl);
                        request.Headers.Add("Authorization", "Token " + token);
                        request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                        request.Headers.Add("x-salt", "683239");
                        request.Headers.Add("x-alpha", "21");


                        var content = new StringContent(" {\r\n    \"message_id\": \"" + uniqueID + "\" \r\n }", null, "application/json");

                        request.Content = content;
                        var response = client.Send(request);
                        var responseBody = await response.Content.ReadAsStringAsync();
                        response.EnsureSuccessStatusCode();
                        Console.WriteLine(await response.Content.ReadAsStringAsync());
                        jsonString = await response.Content.ReadAsStringAsync();
                        Program.LogApiRequest($"ImageURL {jsonString} - fetch Passed - ", Program.logFilePath);

                        //using (JsonDocument doc1 = JsonDocument.Parse(jsn))
                        //{
                        //    var roots = doc1.RootElement;
                        //    foreach (var item in roots.GetProperty("image_urls").EnumerateArray())
                        //    {
                        //        string url = item.GetProperty("url").GetString();
                        //        Console.WriteLine(url);
                        //    }
                        //}
                    }
                }
                if (jsonString == null)
                {
                    jsonString = @"
                                        {
                                            ""image_urls"": [
                                                {
                                                    ""url"": ""http://192.168.31.7/media/plates/2026-03-20-12-53-41_650b_plate.jpeg?md5=LIX_RtSwd-cEwh1VAFGRZA&expires=1774426549"",
                                                    ""type"": ""PLATE""
                                                },
                                                {
                                                    ""url"": ""http://192.168.31.7/media/images/2026-03-20-12-53-41_650b.jpeg?md5=-6_HJbwV66CNGYIDYqSITg&expires=1774426549"",
                                                    ""type"": ""FRAME""
                                                }
                                            ]
                                        }";

                }


                JsonDocument doc = JsonDocument.Parse(jsonString);
                JsonElement root = doc.RootElement;
                JsonElement images = root.GetProperty("image_urls");

                List<imgRoot> data = new List<imgRoot>();
                foreach (var itm in images.EnumerateArray())
                {

                    VehicleEvent vehicleEvents = new VehicleEvent();
                    try
                    {
                        Console.WriteLine($"ID={ve.UniqueId}, Plate={ve.PlateNumber}");
                        vehicleEvents.UniqueId = ve.UniqueId.ToString();
                        vehicleEvents.PlateNumber = ve.PlateNumber;
                        vehicleEvents.CameraId = ve.CameraId.ToString();
                        vehicleEvents.ImageUrl = itm.GetProperty("url").GetString();
                        vehicleEvents.ImageType = itm.GetProperty("type").GetString();   //Plate/Frame
                        vehicleEvents.Timestamp = DateTime.Now;
                        await Program.ProcessEvent(vehicleEvents);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in ImageURL fetch : {vehicleEvents.ImageUrl} - " + ex.Message.ToString());
                        //Program.LogApiRequest($"Error in ImageURL fetch : {vehicleEvents.ImageUrl} - " + ex.Message.ToString(), Program.logFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                var r = ex.Message.ToString();
                Program.LogApiRequest($"Error in ImageURL fetch : {ve.UniqueId} - " + ex.Message.ToString(), Program.logFilePath);
            }
            return null;
        }
    }
    public static async Task ProcessEvent(VehicleEvent ev)
    {
        try
        {
            var client1 = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, ev.ImageUrl);
            var response = await client1.SendAsync(request);
            response.EnsureSuccessStatusCode();
            //Console.WriteLine(await response.Content.ReadAsStringAsync());

            byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();

            // 2. Convert to Base64
            string base64String = Convert.ToBase64String(imageBytes);

            // Optional: Add MIME type prefix if needed
            string base64WithPrefix = $"data:image/jpeg;base64,{base64String}";

            // Print (optional)
            Console.WriteLine(base64String);

            Console.WriteLine($"Image  Processed successfully get and send to DB for {ev.ImageUrl}");

            //HttpClient client = new HttpClient();
            // imageBytes = await client.GetByteArrayAsync(ev.ImageUrl);
            var dataAccess = new ANPRDataAccess(connectionString);
            await dataAccess.InsertIntoDatabase(ev, imageBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine($"Error in ProcessEvent  for {ev.ImageUrl}: {ex.Message}");
            //LogApiRequest($"Error in ProcessEvent  for {ev.ImageUrl}: {ex.Message}", logFilePath);
        }
    }



    public static async Task<List<vehicleAccessImageUpdateList>> GetVehicleAccessImageUpdateList(string Cameraid)
    {
        ANPRDataAccess _anpr = new ANPRDataAccess(connectionString);
        //DataTable dt = _anpr.GetDataTable("Select unique_id,Cameraid,CameraLocation,shortname,VehicleNumber,InoutFlag,InoutTime from vwGetVehicleAccessImageUpdateList where Cameraid =" + Cameraid);

        //DataTable dt = _anpr.GetDataTable("Select TOP 50 unique_id,Cameraid,CameraLocation,shortname,VehicleNumber,InoutFlag,InoutTime from vwGetVehicleAccessImageUpdateList where Cameraid =" + Cameraid + " ORDER BY InoutTime DESC");
        DataTable dt = _anpr.GetDataTable("Select TOP 200 unique_id,Cameraid,CameraLocation,shortname,VehicleNumber,InoutFlag,InoutTime from vwGetVehicleAccessImageUpdateList where Cameraid =" + Cameraid + " ORDER BY InoutTime ASC");
        try
        {
            if (dt != null && dt.Rows.Count > 0)
            {
                List<vehicleAccessImageUpdateList> vehlst = new List<vehicleAccessImageUpdateList>();

                foreach (DataRow row in dt.Rows)
                {
                    vehlst.Add(new vehicleAccessImageUpdateList
                    {
                        unique_id = Convert.ToString(row["unique_id"]),
                        Cameraid = Convert.ToInt16(row["Cameraid"]),
                        CameraLocation = Convert.ToString(row["CameraLocation"]),
                        shortname = Convert.ToString(row["shortname"]),
                        VehicleNumber = Convert.ToString(row["VehicleNumber"]),
                        InoutFlag = Convert.ToString(row["InoutFlag"]),
                        InoutTime = (row["InoutTime"] != DBNull.Value) ? Convert.ToDateTime(row["InoutTime"]) : Convert.ToDateTime(DateTime.Now)
                    });
                }

                return vehlst;
            }
        }

        catch (Exception ex)
        {
            // log or handle the error
            Console.WriteLine($"Error: {ex.Message}");
        }

        return new List<vehicleAccessImageUpdateList>(); // return empty list instead of null
    }

    public static async Task<string> CallGetVehicleEventList(string url, string token, ANPR_Controller Terminal)
    {
        //List<MyItem> items = await ApiClient.GetItemsAsync();
        if (url == null)
            url = "http://192.168.0.104/api/v1/sdk/vehicle";

        int cameraid = Terminal.ID;
        //List<Vehicle> vehicles = new List<Vehicle>();
        if (1 == 1)
        {
            //http://{{host}}/api/v1/sdk/vehicle
            try
            {

                if (int.TryParse(Terminal.ANPRTerminalID, out int result))
                {
                    cameraid = result;
                }
                else
                {
                    Console.WriteLine("Not a valid number.");

                }
                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Token " + token);
                request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                request.Headers.Add("x-salt", "683239");
                request.Headers.Add("x-alpha", "21");
                //var response = await client.SendAsync(request);
                //response.EnsureSuccessStatusCode();
                // request.Content = "";

                // Console.WriteLine("Raw response: " + responseBody);
                var response = client.Send(request);
                response.EnsureSuccessStatusCode();

                //Console.WriteLine(await response.Content.ReadAsStringAsync());
                //var items = await response.Content.ReadAsStringAsync();

                string jsonString = await response.Content.ReadAsStringAsync();

                // Log an API request
                LogApiRequest($"{url} Responce : {jsonString}", logFilePath);

                /*
                                jsonString = @" 
                   {
                    ""count"": 1023,
                    ""next"": ""http://192.168.31.11/api/v1/sdk/events?page=2&timestamp__gte=2025-01-20+00%3A00%3A00"",
                    ""previous"": null,
                    ""results"": [
                        {
                            ""id"": 1055,
                            ""updated_at"": ""2025-11-15T16:07:50.818245+05:30"",
                            ""number_plate"": ""MH12QH4388"",
                            ""auth"": false,
                            ""created_at"": ""2025-11-15T16:07:49.749637+05:30"",
                            ""is_entry"": false,
                            ""vehicle_type"": 1,
                            ""unique_id"": ""2025-11-15-16-07-49_4744"",
                            ""plate_img_uri"": null,
                            ""frame_img_uri"": null,
                            ""model"": ""-1"",
                            ""is_blacklisted"": false,
                            ""upload_status"": {
                                ""event"": false,
                                ""frame"": false,
                                ""plate"": false
                            } 
                        },
                        {
                            ""id"": 1036,
                            ""updated_at"": ""2025-11-15T15:57:00.910286+05:30"",
                            ""number_plate"": ""MH12LV6449"",
                            ""auth"": false,
                            ""created_at"": ""2025-11-15T15:56:55.459122+05:30"",
                            ""is_entry"": false,
                            ""vehicle_type"": 0,
                            ""unique_id"": ""2025-11-15-15-56-54_81d9"",
                            ""plate_img_uri"": null,
                            ""frame_img_uri"": null,
                            ""model"": ""-1"",
                            ""is_blacklisted"": false,
                            ""upload_status"": {
                                ""event"": false,
                                ""frame"": false,
                                ""plate"": false
                            },
                            ""is_synced"": false,
                            ""timestamp"": ""2025-11-15T15:57:00.340762+05:30"",
                            ""retries"": 0,
                            ""sync_log"": {
                                ""error"": false,
                                ""success"": false,
                                ""error_message"": """",
                                ""last_synced_at"": null,
                                ""error_occurred_at"": null
                            },
                            ""is_mygate_synced"": false,
                            ""bbox"": [
                                [
                                    559,
                                    252,
                                    102,
                                    36
                                ]
                            ],
                            ""is_tcin_synced"": false,
                            ""visitor"": null,
                            ""owner"": null,
                            ""society"": 1,
                            ""camera"": 1,
                            ""color"": null,
                            ""vehicle_category"": null,
                            ""vehicle_brand"": null,
                            ""vehicle_model"": null
                        }
                    ]
                   }";

                               */
                //var jsonData = JsonConvert.DeserializeObject<Root>(jsonString);
                using (JsonDocument doc = JsonDocument.Parse(jsonString))
                {
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("results", out JsonElement results))
                    {
                        // Log an API request
                        LogApiRequest($"{url} LogEvents count: {results.EnumerateArray().Count()}", logFilePath);

                        foreach (JsonElement item in results.EnumerateArray())
                        {
                            /*id"": 1036,
             ""updated_at"": ""2025 - 11 - 15T15: 57:00.910286 + 05:30"",
             ""number_plate"": ""MH12LV6449"",
             ""auth"": false,
             ""created_at"": ""2025 - 11 - 15T15: 56:55.459122 + 05:30"",
             ""is_entry"": false,
             ""vehicle_type"": 0,
             ""unique_id"": ""2025 - 11 - 15 - 15 - 56 - 54_81d9"",
             ""plate_img_uri"": null,
             ""frame_img_uri"": null,
             ""model"": "" - 1"",
             ""is_blacklisted"": false,

                             */

                            int _id = item.GetProperty("id").GetInt32();
                            string _numberPlate = item.GetProperty("number_plate").GetString();
                            string _cameraLocation = Terminal.ANPRLocation;
                            BinaryData _cameraImage = null;// item.GetProperty("frame_img_uri");
                            BinaryData _CameraNumberPlateImage = null;// item.GetProperty("plate_img_uri");
                            string _unique_id = item.GetProperty("unique_id").ToString();
                            DateTime _inOutTime = DateTime.Now;// item.GetProperty("updated_at");
                            _inOutTime = DateTime.Parse(
                            item.GetProperty("updated_at").ToString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind);
                            //_inOutTime =Convert.ToDateTime( item.GetProperty("updated_at"));
                            //auth updated_at created_at  unique_id plate_img_uri frame_img_uri model is_blacklisted

                            Console.WriteLine($"ID={_id}, Plate={_numberPlate}");
                            ANPR_VehicleInoutRegister_log InOutLog = new ANPR_VehicleInoutRegister_log
                            {
                                //id CameraID, CameraLocation, CameraImage, CameraLocation, CameraNumberPlateImage, VehicleNumber, OwnerID, InOutFlag, InOutTime
                                id = _id,
                                unique_id = _unique_id,
                                CameraID = cameraid,
                                CameraLocation = _cameraLocation,
                                CameraImage = _cameraImage,
                                CameraNumberPlateImage = _CameraNumberPlateImage,
                                VehicleNumber = _numberPlate.ToString(),
                                OwnerID = null,
                                InOutFlag = Terminal.ANPR_Mode,
                                InOutTime = _inOutTime
                            };

                            //// Build object
                            //AddVehicle vehicle = new AddVehicle
                            //{
                            //    id = id,
                            //    number_plate = numberPlate,
                            //    vehicle_type = item.GetProperty("vehicle_type").GetInt32(),
                            //    auth_type = item.GetProperty("auth_type").GetString(),
                            //    comment = Terminal.ToString() + " : " + item.GetProperty("comment").GetString(),
                            //    owner = item.GetProperty("owner").GetInt32(),

                            //    //Terminal
                            //};



                            // Log an API request
                            LogApiRequest($"{InOutLog.VehicleNumber.ToString()}, Inserting : {insertedID.ToString()}", logFilePath);
                            // Save in DB
                            Program p = new Program();
                            await p.InsertOrUpdateANPRVehicleEventsAsync(connectionString, InOutLog);
                            LogApiRequest($"InOutLog for : {InOutLog.VehicleNumber.ToString()},Inserted : {insertedID.ToString()}", logFilePath);

                        }
                        // Log an API request
                        LogApiRequest($"{url} LogEventsData count: {insertedID.ToString()}", logFilePath);

                    }
                }
                /*
                //"[""number_plate"": ""ME16X3699"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": null, ""owner"": 3, ""id"": 16]"
                if ((response.IsSuccessStatusCode))
                {
                    string jsonString = await response.Content.ReadAsStringAsync();

                    var jsonData = JsonConvert.DeserializeObject<Root>(jsonString);

                    // Extract "results" from the JSON data
                    dynamic results = jsonData.results;
                    await Set_ADDUpdateVehiclesInDB(results, cameraid);
                    return "ANPR_Vehicle Data Add/Update with cameraid"+cameraid.ToString()+" in Database Successfully";
                    // return "ANPR_Vehicle Data Add/Update in Database Successfully";
                }
                else
                {
                    Console.WriteLine("Request for camera:"+ cameraid.ToString() +                        " failed: " + response.StatusCode);
                }
                */
            }
            catch (Exception ex)
            {
                var r = ex.Message.ToString();
                LogApiRequest($"{url} Error :" + ex.Message.ToString(), logFilePath);

            }
            return null;
        }

    }

    public static async Task<string> CallGetVehicleEventList_Test(string url, string token, ANPR_Controller Terminal)
    {
        //List<MyItem> items = await ApiClient.GetItemsAsync();
        if (url == null)
            url = "http://192.168.0.104/api/v1/sdk/vehicle";

        int cameraid = Terminal.ID;
        //List<Vehicle> vehicles = new List<Vehicle>();
        if (1 == 1)
        {
            //http://{{host}}/api/v1/sdk/vehicle
            try
            {
                /******************
                if (int.TryParse(Terminal.ANPRTerminalID, out int result))
                {
                    cameraid = result;
                }
                else
                {
                    Console.WriteLine("Not a valid number.");

                }
                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Token " + token);
                request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                request.Headers.Add("x-salt", "683239");
                request.Headers.Add("x-alpha", "21");
                //var response = await client.SendAsync(request);
                //response.EnsureSuccessStatusCode();
                // request.Content = "";

                // Console.WriteLine("Raw response: " + responseBody);
                var response = client.Send(request);
                response.EnsureSuccessStatusCode();

                Console.WriteLine(await response.Content.ReadAsStringAsync());
                //var items = await response.Content.ReadAsStringAsync();
                

                string jsonString = await response.Content.ReadAsStringAsync();
               */

                string jsonString = @"{
    ""count"": 15637,
    ""next"": ""http://192.168.31.11/api/v1/sdk/events?page=2&timestamp__gte=2025-01-20+00%3A00%3A00"",
    ""previous"": null,
    ""results"": [
        {
            ""id"": 16444,
            ""updated_at"": ""2025-12-01T15:06:53.277059+05:30"",
            ""number_plate"": ""MH12HL5072"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T15:06:51.176332+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-15-06-50_c851"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T15:06:53.035745+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    85,
                    219,
                    81,
                    33
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16443,
            ""updated_at"": ""2025-12-01T15:03:36.983090+05:30"",
            ""number_plate"": ""MH12TT4655"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T15:03:34.891286+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-15-03-34_4f88"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T15:03:36.523989+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    1118,
                    295,
                    64,
                    46
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16442,
            ""updated_at"": ""2025-12-01T15:03:27.841742+05:30"",
            ""number_plate"": ""MH12SY6575"",
            ""auth"": true,
            ""created_at"": ""2025-12-01T15:03:22.578125+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-15-03-21_6e64"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T15:03:27.107391+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    210,
                    223,
                    88,
                    36
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": 1,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16441,
            ""updated_at"": ""2025-12-01T15:02:43.519730+05:30"",
            ""number_plate"": ""MH12MZ0036"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T15:02:38.254069+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-15-02-37_7dc3"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T15:02:42.807711+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    322,
                    229,
                    95,
                    36
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16440,
            ""updated_at"": ""2025-12-01T15:01:49.125781+05:30"",
            ""number_plate"": ""MH12RB5397"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T15:01:49.125815+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-15-01-47_3472"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T15:01:48.535111+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    604,
                    424,
                    126,
                    73
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16439,
            ""updated_at"": ""2025-12-01T15:00:50.009685+05:30"",
            ""number_plate"": ""MH12AN9599"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T15:00:50.009716+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-15-00-48_86ff"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T15:00:49.428800+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    390,
                    448,
                    117,
                    68
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16438,
            ""updated_at"": ""2025-12-01T14:58:52.858988+05:30"",
            ""number_plate"": ""MH12YH4703"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:58:49.644612+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-14-58-48_a22e"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:58:52.798726+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    716,
                    243,
                    83,
                    40
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16437,
            ""updated_at"": ""2025-12-01T14:54:36.046823+05:30"",
            ""number_plate"": ""MH48CR7937"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:54:34.980703+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-54-34_4a52"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:54:35.713041+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    398,
                    381,
                    98,
                    55
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16436,
            ""updated_at"": ""2025-12-01T14:54:08.879632+05:30"",
            ""number_plate"": ""MH12XV8679"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:54:04.624529+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-54-03_65e0"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:54:07.753962+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    194,
                    220,
                    62,
                    37
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16435,
            ""updated_at"": ""2025-12-01T14:52:48.465960+05:30"",
            ""number_plate"": ""MH115269"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:52:47.398822+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-52-46_d4be"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:52:47.338622+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    1015,
                    333,
                    81,
                    44
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16434,
            ""updated_at"": ""2025-12-01T14:52:40.351507+05:30"",
            ""number_plate"": ""MH19EC9251"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:52:38.258137+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-52-37_716a"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:52:39.948347+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    66,
                    228,
                    64,
                    24
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16433,
            ""updated_at"": ""2025-12-01T14:52:30.185445+05:30"",
            ""number_plate"": ""MH12UY7494"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:52:25.976216+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-52-25_c9b2"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:52:29.365934+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    820,
                    251,
                    60,
                    36
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16432,
            ""updated_at"": ""2025-12-01T14:50:43.842173+05:30"",
            ""number_plate"": ""MH44U2540"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:50:38.623026+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-14-50-38_59d1"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:50:43.653796+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    382,
                    237,
                    98,
                    34
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16431,
            ""updated_at"": ""2025-12-01T14:50:03.546359+05:30"",
            ""number_plate"": ""MH12QH3372"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:50:01.372602+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-50-00_0aab"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:50:02.680130+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    56,
                    223,
                    67,
                    27
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16430,
            ""updated_at"": ""2025-12-01T14:48:15.180383+05:30"",
            ""number_plate"": ""MH12PQ3993"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:48:08.812156+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-14-48-07_4e7f"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:48:14.526621+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    346,
                    229,
                    95,
                    37
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16429,
            ""updated_at"": ""2025-12-01T14:47:52.726226+05:30"",
            ""number_plate"": ""MH12LG7"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:47:52.726269+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-47-51_7fcb"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:47:52.070810+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    120,
                    206,
                    46,
                    21
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16428,
            ""updated_at"": ""2025-12-01T14:45:05.409338+05:30"",
            ""number_plate"": ""MH12SK14"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:44:58.012585+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-14-44-57_32f5"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:45:05.317582+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    303,
                    228,
                    71,
                    37
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16427,
            ""updated_at"": ""2025-12-01T14:44:22.891021+05:30"",
            ""number_plate"": ""MH12WF4642"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:44:18.727219+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-44-18_4cbe"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:44:21.910198+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    66,
                    105,
                    29,
                    18
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16426,
            ""updated_at"": ""2025-12-01T14:43:03.300136+05:30"",
            ""number_plate"": ""MH12KW7339"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:43:03.300172+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 1,
            ""unique_id"": ""2025-12-01-14-43-02_ef67"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:43:02.470148+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    1085,
                    354,
                    66,
                    28
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        },
        {
            ""id"": 16425,
            ""updated_at"": ""2025-12-01T14:43:07.584792+05:30"",
            ""number_plate"": ""MH12SY7548"",
            ""auth"": false,
            ""created_at"": ""2025-12-01T14:43:02.233154+05:30"",
            ""is_entry"": false,
            ""vehicle_type"": 0,
            ""unique_id"": ""2025-12-01-14-43-01_838d"",
            ""plate_img_uri"": null,
            ""frame_img_uri"": null,
            ""model"": ""-1"",
            ""is_blacklisted"": false,
            ""upload_status"": {
                ""event"": false,
                ""frame"": false,
                ""plate"": false
            },
            ""is_synced"": false,
            ""timestamp"": ""2025-12-01T14:43:07.071002+05:30"",
            ""retries"": 0,
            ""sync_log"": {
                ""error"": false,
                ""success"": false,
                ""error_message"": """",
                ""last_synced_at"": null,
                ""error_occurred_at"": null
            },
            ""is_mygate_synced"": false,
            ""bbox"": [
                [
                    456,
                    237,
                    103,
                    40
                ]
            ],
            ""is_tcin_synced"": false,
            ""visitor"": null,
            ""owner"": null,
            ""society"": 1,
            ""camera"": 1,
            ""color"": null,
            ""vehicle_category"": null,
            ""vehicle_brand"": null,
            ""vehicle_model"": null
        }
    ]
}";


                //var jsonData = JsonConvert.DeserializeObject<Root>(jsonString);
                using (JsonDocument doc = JsonDocument.Parse(jsonString))
                {
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("results", out JsonElement results))
                    {
                        foreach (JsonElement item in results.EnumerateArray())
                        {
                            /*id"": 1036,
             ""updated_at"": ""2025 - 11 - 15T15: 57:00.910286 + 05:30"",
             ""number_plate"": ""MH12LV6449"",
             ""auth"": false,
             ""created_at"": ""2025 - 11 - 15T15: 56:55.459122 + 05:30"",
             ""is_entry"": false,
             ""vehicle_type"": 0,
             ""unique_id"": ""2025 - 11 - 15 - 15 - 56 - 54_81d9"",
             ""plate_img_uri"": null,
             ""frame_img_uri"": null,
             ""model"": "" - 1"",
             ""is_blacklisted"": false,

                             */

                            int _id = item.GetProperty("id").GetInt32();
                            string _numberPlate = item.GetProperty("number_plate").GetString();
                            string _cameraLocation = Terminal.ANPRLocation;
                            BinaryData _cameraImage = null;// item.GetProperty("frame_img_uri");
                            BinaryData _CameraNumberPlateImage = null;// item.GetProperty("plate_img_uri");
                            string _unique_id = item.GetProperty("unique_id").ToString();
                            DateTime _inOutTime = DateTime.Now;// item.GetProperty("updated_at");
                            _inOutTime = DateTime.Parse(
                            item.GetProperty("updated_at").ToString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind);

                            //_inOutTime =Convert.ToDateTime( item.GetProperty("updated_at"));
                            //auth updated_at created_at  unique_id plate_img_uri frame_img_uri model is_blacklisted

                            Console.WriteLine($"ID={_id}, Plate={_numberPlate}");

                            ANPR_VehicleInoutRegister_log InOutLog = new ANPR_VehicleInoutRegister_log
                            {
                                //id CameraID, CameraLocation, CameraImage, CameraLocation, CameraNumberPlateImage, VehicleNumber, OwnerID, InOutFlag, InOutTime
                                id = _id,
                                unique_id = _unique_id,
                                CameraID = cameraid,
                                CameraLocation = _cameraLocation,
                                CameraImage = _cameraImage,
                                CameraNumberPlateImage = _CameraNumberPlateImage,
                                VehicleNumber = _numberPlate.ToString(),
                                OwnerID = null,
                                InOutFlag = Terminal.ANPR_Mode,
                                InOutTime = _inOutTime
                            };

                            //// Build object
                            //AddVehicle vehicle = new AddVehicle
                            //{
                            //    id = id,
                            //    number_plate = numberPlate,
                            //    vehicle_type = item.GetProperty("vehicle_type").GetInt32(),
                            //    auth_type = item.GetProperty("auth_type").GetString(),
                            //    comment = Terminal.ToString() + " : " + item.GetProperty("comment").GetString(),
                            //    owner = item.GetProperty("owner").GetInt32(),

                            //    //Terminal
                            //};




                            // Save in DB
                            Program p = new Program();
                            await p.InsertOrUpdateANPRVehicleEventsAsync(connectionString, InOutLog);
                        }
                    }
                }
                /*
                //"[""number_plate"": ""ME16X3699"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": null, ""owner"": 3, ""id"": 16]"
                if ((response.IsSuccessStatusCode))
                {
                    string jsonString = await response.Content.ReadAsStringAsync();

                    var jsonData = JsonConvert.DeserializeObject<Root>(jsonString);

                    // Extract "results" from the JSON data
                    dynamic results = jsonData.results;
                    await Set_ADDUpdateVehiclesInDB(results, cameraid);
                    return "ANPR_Vehicle Data Add/Update with cameraid"+cameraid.ToString()+" in Database Successfully";
                    // return "ANPR_Vehicle Data Add/Update in Database Successfully";
                }
                else
                {
                    Console.WriteLine("Request for camera:"+ cameraid.ToString() +                        " failed: " + response.StatusCode);
                }
                */
            }
            catch (Exception ex)
            {
                var r = ex.Message.ToString();
            }
            return null;
        }

    }


    public static async Task<string> CallGetApiAsync(string url, string token, string Terminal)
    {
        //List<MyItem> items = await ApiClient.GetItemsAsync();
        if (url == null)
            url = "http://192.168.0.104/api/v1/sdk/vehicle";
        int cameraid = 1;

        if (1 == 1)
        {
            //http://{{host}}/api/v1/sdk/vehicle
            try
            {

                if (int.TryParse(Terminal, out int result))
                {
                    cameraid = result;
                }
                else
                {
                    Console.WriteLine("Not a CameraID number. : " + Terminal);

                }

                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Token " + token);
                request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                request.Headers.Add("x-salt", "683239");
                request.Headers.Add("x-alpha", "21");
                // var response = await client.SendAsync(request);
                var response = client.Send(request);

                response.EnsureSuccessStatusCode();

                //Console.WriteLine(await response.Content.ReadAsStringAsync());

                //var items = await response.Content.ReadAsStringAsync(); 
                string jsonString = await response.Content.ReadAsStringAsync();

                // Log an API request
                //LogApiRequest($"{baseUrl} method  - Responce {jsonString}", logFilePath);

                //`3-comment this line
                /* 
                                                string jsonString = @"
                                {
                                  ""count"": 2,
                                  ""next"": null,
                                  ""previous"": null,
                                  ""results"": [
                                    {
                                      ""number_plate"": ""MH16X3639"",
                                      ""vehicle_type"": 1,
                                      ""auth_type"": ""whitelist"",
                                      ""comment"": null,
                                      ""owner"": null,
                                      ""id"": 2
                                    },
                                    {
                                      ""number_plate"": ""MH16X3629"",
                                      ""vehicle_type"": 0,
                                      ""auth_type"": ""whitelist"",
                                      ""comment"": null,
                                      ""owner"": null,
                                      ""id"": 1
                                    }
                                  ]
                                }";
                 */
                //var jsonData = JsonConvert.DeserializeObject<Root>(jsonString);
                using (JsonDocument doc = JsonDocument.Parse(jsonString))
                {
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("results", out JsonElement results))
                    {
                        foreach (JsonElement item in results.EnumerateArray())
                        {
                            int id = item.GetProperty("id").GetInt32();
                            string numberPlate = item.GetProperty("number_plate").GetString();
                            Console.WriteLine($"ID={id}, Plate={numberPlate}");

                            // Build object
                            AddVehicle vehicle = new AddVehicle
                            {
                                id = id,
                                number_plate = numberPlate,
                                vehicle_type = item.GetProperty("vehicle_type").GetInt32(),
                                auth_type = item.GetProperty("auth_type").GetString(),
                                comment = Terminal.ToString() + " : " + item.GetProperty("comment").GetString(),
                                owner = 1
                                //item.GetProperty("owner").GetInt32()
                                //Terminal
                            };

                            // Save in DB
                            Program p = new Program();
                            await p.InsertOrUpdateANPRVehicleAsync(connectionString, vehicle, cameraid);
                        }
                    }
                }
                /*
                //"[""number_plate"": ""ME16X3699"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": null, ""owner"": 3, ""id"": 16]"
                if ((response.IsSuccessStatusCode))
                {
                    string jsonString = await response.Content.ReadAsStringAsync();

                    var jsonData = JsonConvert.DeserializeObject<Root>(jsonString);

                    // Extract "results" from the JSON data
                    dynamic results = jsonData.results;
                    await Set_ADDUpdateVehiclesInDB(results, cameraid);
                    return "ANPR_Vehicle Data Add/Update with cameraid"+cameraid.ToString()+" in Database Successfully";
                    // return "ANPR_Vehicle Data Add/Update in Database Successfully";
                }
                else
                {
                    Console.WriteLine("Request for camera:"+ cameraid.ToString() +                        " failed: " + response.StatusCode);
                }
                */
            }
            catch (Exception ex)
            {
                var r = ex.Message.ToString();
            }
            return null;
        }

    }

    public static async Task<List<ANPR_Controller>> GetANPRList()
    {
        ANPRDataAccess _anpr = new ANPRDataAccess(connectionString);
        DataTable dt = _anpr.GetDataTable("Select * from vwANPRCameras Where isactive = 1");
        List<ANPR_Controller> lst = new List<ANPR_Controller>();

        try
        {
            if (dt != null && dt.Rows.Count > 0)
            {
                foreach (DataRow row in dt.Rows)
                {
                    //lst.Add(new ANPR_Controller
                    //{
                    //    ID = Convert.ToInt32(row["ID"]),
                    //    ANPRTerminalID = Convert.ToString(row["ANPRTerminalID"]),
                    //    ANPRIP = Convert.ToString(row["ANPRIP"]),
                    //    ANPRport = Convert.ToString(row["ANPRport"]),
                    //    ANPRLocation = Convert.ToString(row["ANPRLocation"]),
                    //    isActive = Convert.ToBoolean(row["isActive"]),
                    //    IsDeleted = Convert.ToBoolean(row["isDeleted"]),
                    //    ANPR_Mode = Convert.ToString(row["ANPRMode"]),
                    //    last_downloadedOn = (row["last_downloadedOn"] != DBNull.Value) ? Convert.ToDateTime(row["last_downloadedOn"]) : Convert.ToDateTime(DateTime.Now.AddHours(1)),
                    //    last_updatedOn = (row["last_updatedOn"] != DBNull.Value) ? Convert.ToDateTime(row["last_updatedOn"]) : Convert.ToDateTime(DateTime.Now.AddHours(1))
                    //});

                    lst.Add(new ANPR_Controller
                    {
                        ID = Convert.ToInt32(row["ID"]),
                        ANPRTerminalID = Convert.ToString(row["ANPRTerminalID"]),
                        ANPRIP = Convert.ToString(row["ANPRIP"]),
                        ANPRport = Convert.ToString(row["ANPRport"]),
                        ANPRLocation = Convert.ToString(row["ANPRLocation"]),
                        isActive = Convert.ToBoolean(row["isActive"]),
                        IsDeleted = Convert.ToBoolean(row["isDeleted"]),
                        ANPR_Mode = Convert.ToString(row["ANPRMode"]),
                        last_downloadedOn = (row["last_downloadedOn"] != DBNull.Value) ? Convert.ToDateTime(row["last_downloadedOn"]) : Convert.ToDateTime(DateTime.Now.AddHours(-4)),
                        last_updatedOn = (row["last_updatedOn"] != DBNull.Value) ? Convert.ToDateTime(row["last_updatedOn"]) : Convert.ToDateTime(DateTime.Now.AddHours(1))
                    });
                }

                return lst;
            }
        }

        catch (Exception ex)
        {
            // log or handle the error
            Console.WriteLine($"Error: {ex.Message}");
        }

        return new List<ANPR_Controller>(); // return empty list instead of null
    }

    public class Root
    {
        public int count { get; set; }
        public string next { get; set; }
        public string previous { get; set; }
        public List<Vehicle> results { get; set; }
    }

    public static async Task Set_ADDUpdateVehiclesInDB(dynamic results, int CameraId = 1)
    {
        // Populate DataTable with data from results
        if (results != null)
        {
            if (results.Count > 0)
            {
                string targetPlate = "ME16X3699";
                // Fix for CS1977: Cast the dynamic object to a known type or use a delegate explicitly.
                if (results != null)
                {

                    var matchedVehicle = ((IEnumerable<dynamic>)results).FirstOrDefault(v => v.number_plate.ToString() == targetPlate.ToString());

                    if (matchedVehicle != null)
                    {
                        int id = matchedVehicle.id;
                        Console.WriteLine($"Vehicle ID for plate {targetPlate}: {id}");
                    }
                    else
                    {
                        Console.WriteLine($"Vehicle with plate {targetPlate} not found.");
                    }
                }


                insertedID = 0; updatedID = 0;
                foreach (var item in results)
                {
                    #region VEH_cls_sample



                    ANPR_vehicle _veh = new ANPR_vehicle()
                    {
                        NumberPlate = ((Vehicle)item).number_plate,
                        VType = 0,
                        Auth = true,
                        IsEntry = true,
                        ClientName = ((Vehicle)item).owner.ToString() + "",
                        SourceId = 1,
                        CameraId = CameraId

                    };

                    //(Convert.ToInt16(((Vehicle)item).id) > 0), 
                    // Convert.ToInt16(item.id)
                    #endregion
                    /*
                    ANPR_vehicle _veh = new ANPR_vehicle()
                    {
                        NumberPlate = "mh-20AJ1234",
                        VType = 1,
                        Auth = true,
                        IsEntry = (Convert.ToInt16(1) > 0 ? false : true),
                        ClientName = "N Name",
                        SourceId = 11,//Convert.ToInt16(item.id),
                        CameraId = 1,
                    };
                    */

                    ANPRDataAccess AD = new ANPRDataAccess(connectionString);
                    //Add update ANPR_vehicle

                    await AD.InsertOrUpdateANPRVehicleAsync(_veh);

                    //  ADDUpdateVehiclesInDB vehicle in ANPR_vehicle
                }
                var r = "ANPR_Vehicle Data Add -" + insertedID.ToString() + "/Update-" + updatedID.ToString() + " in Database Successfully";
                Console.WriteLine("ANPR_Vehicle Data Add -" + insertedID.ToString() + "/Update-" + updatedID.ToString() + " in Database Successfully");

            }
        }
    }

    #region datatbl_commentedCode

    //public static void ProcessVehicles(DataTable dt)
    //{
    //    //foreach (var vehicle in vehicles)
    //    //{
    //    //    Console.WriteLine($"Number Plate: {vehicle.number_plate}, Vehicle Type: {vehicle.vehicle_type}, Owner: {vehicle.owner}");
    //    //    // Add your processing logic here
    //    //}


    //    // Example: Insert DataTable into SQL Server
    //    DataTable dataTable = dt; // Assume this is your populated DataTable

    //    using (SqlConnection conn = new SqlConnection(connectionString))
    //    {
    //        conn.Open();

    //        foreach (DataRow row in dataTable.Rows)
    //        { 
    //            string query = "INSERT INTO  tblANPR_vehicle " +
    //                " (auth, frame, image, @is_entry, number_plate, timestamp" +
    //                ", type, client_name , site , site_name , camera_id, brand " +
    //                ", client , source_id , node_name , node , class ,color " +
    //                ",hashed_number_plate , speed,vehicle ,vtype  ,IsSync)";
    //            "Values(@auth,@frame, @image,(case when @id >0 then True else False end) " +
    //            ",@number_plate ,Datetme.Now , 1,'client_name' " +
    //            ",@site ='RiverView city'" + ",@site_name= 'Main Gate'" +
    //            ",@camera_id=104" + ",@brand=1" + ",@client= 'client'" + ",@source_id =1" +
    //            ",@node_name=''" + ",@node =0" + ",@class=0" + ",@color = 0" +
    //            ",@hashed_number_plate" + ",@speed=0" + ",@vehicle='Car/Bike'" + ",@vtype=0" + ",@IsSync='N'" +


    //            using (SqlCommand cmd = new SqlCommand(query, conn))
    //            {
    //                cmd.Parameters.AddWithValue("@auth", 1);
    //                cmd.Parameters.AddWithValue("@frame", null);
    //                cmd.Parameters.AddWithValue("@image", null);
    //                cmd.Parameters.AddWithValue("@is_entry", null);
    //                cmd.Parameters.AddWithValue("@number_plate", row["number_plate"]);
    //                cmd.Parameters.AddWithValue("@timestamp", DateTime.Now);

    //                cmd.Parameters.AddWithValue("@type", null);
    //                cmd.Parameters.AddWithValue("@client_name", null);
    //                cmd.Parameters.AddWithValue("@site", "RiverView city");
    //                cmd.Parameters.AddWithValue("@site_name", "Main Gate");
    //                cmd.Parameters.AddWithValue("@camera_id", "1234");
    //                cmd.Parameters.AddWithValue("@brand", "1");

    //                cmd.Parameters.AddWithValue("@client", row["owner"]);
    //                cmd.Parameters.AddWithValue("@source_id", row["id"]);                    
    //                cmd.Parameters.AddWithValue("@node_name", 0);
    //                cmd.Parameters.AddWithValue("@node", 0);
    //                cmd.Parameters.AddWithValue("@class", 0);
    //                cmd.Parameters.AddWithValue("@color", 0);

    //                cmd.Parameters.AddWithValue("@hashed_number_plate", "");
    //                cmd.Parameters.AddWithValue("@speed", 0);
    //                cmd.Parameters.AddWithValue("@vehicle", "Car/Bike");
    //                cmd.Parameters.AddWithValue("@vtype", 0);
    //                cmd.Parameters.AddWithValue("@IsSync", "N");

    //            /*"(1,null, null,(case when @id >0 then True else False end) " +
    //            ",@number_plate ,Datetme.Now , 1,'client_name' " +
    //            ",@site ='RiverView city'" +          ",@site_name= 'Main Gate'" +
    //            ",@camera_id=104" +          ",@brand=1" +          ",@client= 'client'" +          ",@source_id =1" +
    //            ",@node_name=''" +          ",@node =0" +          ",@class=0" +          ",@color = 0" +
    //            ",@hashed_number_plate" +          ",@speed=0" +          ",@vehicle='Car/Bike'" +          ",@vtype=0" +          ",@IsSync='N'" +
    //            */
    //            cmd.ExecuteNonQuery();
    //            }
    //        }
    //    }

    //    Console.WriteLine("Data inserted successfully!");
    //}

    #endregion datatbl_commentedCode
    public static async Task<List<Vehicle>> GetVehicleAsync(string url, string token, int CameraId = 1)
    {
        //Console.WriteLine("GetVehicleAsync");
        List<Vehicle> Vehicles = new List<Vehicle>();
        try
        {
            /*
             * using var request = new HttpRequestMessage(HttpMethod.Get, url);
              request.Headers.Authorization = new AuthenticationHeaderValue("token", token);

              var response = await client.SendAsync(request);
              response.EnsureSuccessStatusCode(); // Throws if not 200-299

              // read response
              var responseBody = await response.Content.ReadAsStringAsync();
              // parse JSON
              using var doc = JsonDocument.Parse(responseBody);
              return Vehicles;
            */

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            // read response
            var responseBody = await response.Content.ReadAsStringAsync();
            // parse JSON
            using var doc = JsonDocument.Parse(responseBody);
            return Vehicles;
        }
        catch (Exception ex)
        {
            var r = ex.Message.ToString();
        }

        return null;

    }

    public static async Task<string> DeleteVehicleListAsync(string url, string token)
    {
        Console.WriteLine(DateTime.Now.ToString() + ": ANPR_DeleteVehicle");

        // 📄 Replace with your actual view name
        string sql = "SELECT * FROM ANPR_DeleteVehicle";

        DataTable dataTable = new DataTable();
        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                using (SqlDataAdapter adapter = new SqlDataAdapter(sql, connection))
                {
                    connection.Open();
                    adapter.Fill(dataTable); // Fetch records into DataTable
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        try
        {
            if (dataTable != null)
            {
                if (dataTable.Rows.Count > 0)
                {
                    Console.WriteLine($"Total Records Fetched: {dataTable.Rows.Count}");
                    foreach (DataRow row in dataTable.Rows)
                    {
                        //var col2 = reader.GetValue(1);
                        //Console.WriteLine($"{col1} | {col2}");
                        int deleteSourceid = 0;

                        int owner = 0;
                        if (row[4] != null && !string.IsNullOrWhiteSpace(row[4].ToString()))
                        {
                            owner = Convert.ToInt16(row[4].ToString());
                        }


                        // 0- number_plate, 1- vehicle_type, 2- auth_type, 3-comment, 4-Owner id
                        AddVehicle vh = new AddVehicle
                        {
                            number_plate = row[0].ToString(),
                            vehicle_type = 0,
                            auth_type = "blacklist",
                            comment = null,
                            owner = owner,
                            id = Convert.ToInt16(row[5].ToString())
                        };

                        //deleteSourceid
                        try
                        {

                            if (vh != null)
                            {
                                if (vh.number_plate != "")
                                {
                                    deleteSourceid = Convert.ToInt16(vh.id);

                                    var client = new HttpClient();
                                    var request = new HttpRequestMessage(HttpMethod.Delete, url + "/" + deleteSourceid.ToString());
                                    request.Headers.Add("Authorization", "token " + token);
                                    request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                                    request.Headers.Add("x-salt", "683239");
                                    request.Headers.Add("x-alpha", "21");

                                    // string json = JsonSerializer.Serialize(rqstvh);
                                    HttpContent content;// new StringContent(json.ToString(), null, "application/json");
                                                        //var content = new StringContent("{\n  \"number_plate\": \"MH11Z3613\",\n  \"vehicle_type\": 0,\n  \"auth_type\": \"whitelist\",\n  \"comment\": null,\n  \"owner\": 3\n}", null, "application/json");


                                    request.Content = null;
                                    var response = client.Send(request);

                                    //if (response.StatusCode == HttpStatusCode.NoContent)
                                    //{
                                    //    Console.WriteLine("No content returned (204). Nothing to parse.");
                                    //    return; // or handle accordingly
                                    //}


                                    //if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.201 )
                                    //{

                                    response.EnsureSuccessStatusCode();
                                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                                    //Console.WriteLine(await response.Content.ReadAsStringAsync());

                                    string result = await response.Content.ReadAsStringAsync();
                                    Console.WriteLine(result);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        Console.WriteLine(vh.number_plate.ToString() + "/id:" + deleteSourceid.ToString() + "- Deleted successfully!");
                                        // Add or Update Vehicle in local DB
                                        Program p = new Program();
                                        // await p.InsertOrUpdateANPRVehicleAsync(connectionString, vh, 1);

                                    }
                                    //return (result);                                 

                                    // }
                                }
                                // return   $"No vehicle fount to register {url}:";
                            }
                        }
                        catch (Exception ex)
                        {
                            return $"Error calling {url}: {ex.Message}";
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No records found.");
                    return $"Error  {url}: {"VehicleList not get"}";
                }
            }
            else
            {
                Console.WriteLine("No records found.");
                return $"Error  {url}: {"VehicleList not get"}";
            }

        }
        catch (SqlException ex)
        {
            Console.WriteLine($"SQL Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }
        return $"Error  {url}: {"VehicleList not get Unknown Error"}";

    }

    //public static async Task<string> DeleteBulkVehicleListAsync(string url, string token)
    public static async Task<string> DeleteBulkVehicleListAsync(string url, string token, int cameraId)
    {
        Console.WriteLine(DateTime.Now.ToString() + ": ANPR_DeleteListVehicle");

        // 📄 Replace with your actual view name
        //string sql = "SELECT id FROM ANPR_DeleteListVehicle";
        //string sql = "SELECT id FROM ANPR_DeleteListVehicle WHERE id IN (SELECT source_id FROM tblANPR_vehicle WHERE camera_id = " + cameraId + ")";
        string sql = @"
    SELECT AV.source_id AS id
    FROM tblANPR_vehicle AV
    WHERE AV.camera_id = " + cameraId + @"
      AND AV.source_id > 0
      AND AV.messageid IN (SELECT id FROM ANPR_DeleteListVehicle)";

        DataTable dataTable = new DataTable();
        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                using (SqlDataAdapter adapter = new SqlDataAdapter(sql, connection))
                {
                    connection.Open();
                    adapter.Fill(dataTable); // Fetch records into DataTable
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        try
        {
            if (dataTable != null)
            {
                if (dataTable.Rows.Count > 0)
                {
                    Console.WriteLine($"Total Records Fetched: {dataTable.Rows.Count}");

                    // 0- number_plate, 1- vehicle_type, 2- auth_type, 3-comment, 4-Owner 5-id
                    string deleteSourceids = "";
                    foreach (DataRow row in dataTable.Rows)
                    {
                        //var col2 = reader.GetValue(1);
                        //Console.WriteLine($"{col1} | {col2}");
                        if (deleteSourceids != "")
                        {
                            deleteSourceids = deleteSourceids + ',' + row["id"].ToString();
                        }
                        else
                        {
                            deleteSourceids = row["id"].ToString();
                        }
                    }

                    //deleteSourceid
                    try
                    {

                        if (deleteSourceids != null)
                        {
                            if (deleteSourceids.Length > 0)
                            {
                                //url = "$\"{url}?[{ids=\"{"+deleteSourceids+"}\"}]";

                                var client = new HttpClient();
                                var request = new HttpRequestMessage(HttpMethod.Post, url);
                                //                                var request = new HttpRequestMessage(HttpMethod.Post, "http://192.168.0.104/api/v1/sdk/vehicle/bulk-delete"); ;

                                request.Headers.Add("Authorization", "token " + token);
                                request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                                request.Headers.Add("x-salt", "683239");
                                request.Headers.Add("x-alpha", "21");

                                // string json = JsonSerializer.Serialize(rqstvh);
                                HttpContent content = new StringContent("{\n    \"ids\": [" + deleteSourceids + "]\n}", null, "application/json");
                                request.Content = content;                //var content = new StringContent("{\n  \"number_plate\": \"MH11Z3613\",\n  \"vehicle_type\": 0,\n  \"auth_type\": \"whitelist\",\n  \"comment\": null,\n  \"owner\": 3\n}", null, "application/json");
                                var response = await client.SendAsync(request);

                                //response.EnsureSuccessStatusCode();
                                //Console.WriteLine(await response.Content.ReadAsStringAsync());
                                ////Console.WriteLine(await response.Content.ReadAsStringAsync());

                                //string result = await response.Content.ReadAsStringAsync();
                                //Console.WriteLine(result);
                                //if (response.IsSuccessStatusCode)
                                //{
                                //    Console.WriteLine("/id:" + deleteSourceids.ToString() + "- Deleted from camera successfully!");
                                //    // Add or Update Vehicle in local DB
                                //    Program p = new Program();
                                //    //await p.DeletedANPRVehiclesAsync(connectionString, deleteSourceids, 1);
                                //    await p.DeletedANPRVehiclesAsync(connectionString, deleteSourceids, cameraId);

                                //    Console.WriteLine("/id:" + deleteSourceids.ToString() + "- Deleted from ANPR successfully!");

                                //}

                                string result = await response.Content.ReadAsStringAsync();
                                Console.WriteLine(result);
                                LogApiRequest($"BULK-DELETE RESPONSE for camera {cameraId}: HTTP {(int)response.StatusCode} | ids sent: {deleteSourceids} | body: {result}", logFilePath);

                                if (response.IsSuccessStatusCode)
                                {
                                    Console.WriteLine("/id:" + deleteSourceids.ToString() + "- Deleted from camera successfully!");
                                    // Add or Update Vehicle in local DB
                                    Program p = new Program();
                                    //await p.DeletedANPRVehiclesAsync(connectionString, deleteSourceids, 1);
                                    await p.DeletedANPRVehiclesAsync(connectionString, deleteSourceids, cameraId);

                                    Console.WriteLine("/id:" + deleteSourceids.ToString() + "- Deleted from ANPR successfully!");

                                }
                                else
                                {
                                    LogApiRequest($"BULK-DELETE FAILED for camera {cameraId}: HTTP {(int)response.StatusCode} | body: {result}", logFilePath);
                                }
                                //return (result);                                 

                                // }
                            }
                            // return   $"No vehicle fount to register {url}:";
                        }


                    }
                    catch (Exception ex)
                    {
                        return $"Error calling {url}: {ex.Message}";
                    }
                }
            }
            else
            {
                Console.WriteLine("No records found.");
                return $"Error  {url}: {"VehicleList not get"}";
            }

        }
        catch (SqlException ex)
        {
            Console.WriteLine($"SQL Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }
        return $"Error  {url}: {"VehicleList not get Unknown Error"}";

    }

    public static async Task ReconcileCameraToMasterListAsync(ANPR_Controller camera, string neighbourhood)
    {
        string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";
        string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";

        string token = await GetTokenAsync(authUrl, clientId, clientSecret);
        if (string.IsNullOrEmpty(token))
        {
            LogApiRequest($"Reconcile: token fetch failed for {camera.ANPRIP}", logFilePath);
            return;
        }

        var authorizedPlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dataAccess = new ANPRDataAccess(connectionString);
        DataTable dt = dataAccess.GetDataTable(
            "SELECT number_plate FROM ANPR_MasterVehicleList WHERE Neighbourhood = '" + neighbourhood + "'");

        if (dt != null)
        {
            foreach (DataRow row in dt.Rows)
            {
                string plate = row["number_plate"].ToString().Replace(" ", "").Trim().ToUpper();
                if (!string.IsNullOrEmpty(plate)) authorizedPlates.Add(plate);
            }
        }

        LogApiRequest($"Reconcile {camera.ANPRIP}: {authorizedPlates.Count} authorized plates for '{neighbourhood}'.", logFilePath);

        // SAFETY GUARD: master list khaali hai toh kuch bhi delete/upload mat karo.
        if (authorizedPlates.Count == 0)
        {
            LogApiRequest($"Reconcile {camera.ANPRIP}: ABORTED - 0 authorized plates for '{neighbourhood}'. Refusing to act (safety guard).", logFilePath);
            return;
        }

        var toDelete = new List<int>();
        var onCameraPlates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalOnCamera = 0;
        string url = baseUrl + "vehicle";

        while (!string.IsNullOrEmpty(url))
        {
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");

            HttpResponseMessage response;
            try { response = await httpClient.SendAsync(request); }
            catch (Exception netEx)
            {
                LogApiRequest($"Reconcile {camera.ANPRIP}: network error - {netEx.Message}", logFilePath);
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogApiRequest($"Reconcile {camera.ANPRIP}: GET vehicle list failed HTTP {(int)response.StatusCode}", logFilePath);
                break;
            }

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("results", out JsonElement results))
            {
                foreach (JsonElement item in results.EnumerateArray())
                {
                    totalOnCamera++;
                    int id = item.GetProperty("id").GetInt32();
                    string plate = (item.GetProperty("number_plate").GetString() ?? "")
                                    .Replace(" ", "").Trim().ToUpper();

                    onCameraPlates.Add(plate);

                    if (!authorizedPlates.Contains(plate))
                        toDelete.Add(id);
                }
            }

            url = (root.TryGetProperty("next", out JsonElement nextEl) && nextEl.ValueKind == JsonValueKind.String)
                ? nextEl.GetString() : null;
        }

        LogApiRequest($"Reconcile {camera.ANPRIP}: {totalOnCamera} total on camera, {toDelete.Count} extra to delete (target {authorizedPlates.Count}).", logFilePath);

        // ---------- DELETE extra ----------
        const int batchSize = 200;
        for (int i = 0; i < toDelete.Count; i += batchSize)
        {
            var batch = toDelete.Skip(i).Take(batchSize);
            string ids = string.Join(",", batch);

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle/bulk-delete");
            request.Headers.Add("Authorization", "token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            request.Content = new StringContent("{\n    \"ids\": [" + ids + "]\n}", null, "application/json");

            var response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            LogApiRequest($"Reconcile {camera.ANPRIP}: batch delete HTTP {(int)response.StatusCode} | ids: {ids} | body: {result}", logFilePath);
        }

        //// ---------- UPLOAD missing (yahi Falcon recovery karega) ----------
        //var toUpload = authorizedPlates.Except(onCameraPlates, StringComparer.OrdinalIgnoreCase).ToList();
        //LogApiRequest($"Reconcile {camera.ANPRIP}: {toUpload.Count} missing vehicle(s) to re-upload.", logFilePath);

        //int uploadSuccess = 0, uploadFail = 0;
        //foreach (var plate in toUpload)
        //{
        //    var client = new HttpClient();
        //    var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle");
        //    request.Headers.Add("Authorization", "token " + token);
        //    request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
        //    request.Headers.Add("x-salt", "683239");
        //    request.Headers.Add("x-alpha", "21");

        //    //var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": """", ""owner"": ""1""}}";

        //    var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": ""Reconcile"", ""owner"": ""1""}}";
        //    request.Content = new StringContent(json, null, "application/json");

        //    var response = await client.SendAsync(request);
        //    string result = await response.Content.ReadAsStringAsync();
        //    if (response.IsSuccessStatusCode) uploadSuccess++; else uploadFail++;
        //    LogApiRequest($"Reconcile {camera.ANPRIP}: upload {plate} HTTP {(int)response.StatusCode} | {result}", logFilePath);
        //}


        //LogApiRequest($"Reconcile {camera.ANPRIP}: DONE. Deleted {toDelete.Count}, Uploaded {uploadSuccess}/{toUpload.Count} (failed {uploadFail}). Target was {authorizedPlates.Count}.", logFilePath);
        // ---------- UPLOAD missing ----------
        //var toUpload = authorizedPlates.Except(onCameraPlates, StringComparer.OrdinalIgnoreCase).ToList();
        //LogApiRequest($"Reconcile {camera.ANPRIP}: {toUpload.Count} missing vehicle(s) to re-upload.", logFilePath);

        //int uploadSuccess = 0, uploadFail = 0;
        //string reconcileOwnerId = toUpload.Count > 0
        //    ? await CreateOwnerAndGetIdAsync(baseUrl, token, "AutoSync-Owner", "NA")
        //    : null;

        //if (toUpload.Count > 0 && string.IsNullOrEmpty(reconcileOwnerId))
        //{
        //    LogApiRequest($"Reconcile {camera.ANPRIP}: no valid owner - skipping upload, retry next cycle.", logFilePath);
        //}
        //else
        //{
        //    foreach (var plate in toUpload)
        //    {
        //        var client = new HttpClient();
        //        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle");
        //        request.Headers.Add("Authorization", "token " + token);
        //        request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
        //        request.Headers.Add("x-salt", "683239");
        //        request.Headers.Add("x-alpha", "21");
        //        var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": 0, ""auth_type"": ""whitelist"", ""comment"": ""Reconcile"", ""owner"": ""{reconcileOwnerId}""}}";
        //        request.Content = new StringContent(json, null, "application/json");
        //        var response = await client.SendAsync(request);
        //        string result = await response.Content.ReadAsStringAsync();
        //        if (response.IsSuccessStatusCode) uploadSuccess++; else uploadFail++;
        //        LogApiRequest($"Reconcile {camera.ANPRIP}: upload {plate} HTTP {(int)response.StatusCode} | {result}", logFilePath);
        //    }
        //}



        var toUpload = authorizedPlates.Except(onCameraPlates, StringComparer.OrdinalIgnoreCase).ToList();
        LogApiRequest($"Reconcile {camera.ANPRIP}: {toUpload.Count} missing vehicle(s) to re-upload.", logFilePath);

        // Same source as UploadVehiclesWithOwnerNamesAsync - single view, no more vwCardHolders/Bld join
        string masterSql = @"
    SELECT number_plate, vehicle_type, FullName, FlatNumber, OwnerType
    FROM ANPR_MasterVehicleList
    WHERE Neighbourhood = '" + neighbourhood.Replace("'", "''") + @"'";
        DataTable masterDt = dataAccess.GetDataTable(masterSql);

        var plateInfo = new Dictionary<string, (string Name, string Flat, string OwnerType, int VType)>(StringComparer.OrdinalIgnoreCase);
        if (masterDt != null)
            foreach (DataRow row in masterDt.Rows)
            {
                string p = row["number_plate"]?.ToString().Trim().ToUpper();
                if (string.IsNullOrEmpty(p) || plateInfo.ContainsKey(p)) continue;
                string n = row["FullName"] == DBNull.Value ? "" : row["FullName"].ToString().Trim();
                string f = row["FlatNumber"] == DBNull.Value ? "" : row["FlatNumber"].ToString().Trim();
                string ot = row["OwnerType"] == DBNull.Value ? "Member" : row["OwnerType"].ToString().Trim();
                int vt = row["vehicle_type"] != DBNull.Value ? Convert.ToInt32(row["vehicle_type"]) : 0;
                plateInfo[p] = (string.IsNullOrEmpty(n) ? "Resident" : n, string.IsNullOrEmpty(f) ? "NA" : f, ot, vt);
            }

        //int uploadSuccess = 0, uploadFail = 0;
        //var ownerIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        //long reconcilePhoneSeq = 7000000000;

        int uploadSuccess = 0, uploadFail = 0;
        var ownerIdCache = await GetExistingOwnersAsync(baseUrl, token); // same fix - reuse existing owners
        long reconcilePhoneSeq = 7000000000;

        foreach (var plate in toUpload)
        {
            var info = plateInfo.TryGetValue(plate, out var pi) ? pi : ("Resident", "NA", "Member", 0);

            string ownerKey = info.Item1 + "|" + info.Item2;
            if (!ownerIdCache.TryGetValue(ownerKey, out string ownerId))
            {
                reconcilePhoneSeq++;
                ownerId = await CreateOwnerAndGetIdAsync(baseUrl, token, info.Item1, info.Item2, reconcilePhoneSeq.ToString(), info.Item3, null, null);
                if (string.IsNullOrEmpty(ownerId))
                {
                    LogApiRequest($"Reconcile {camera.ANPRIP}: owner creation failed for '{info.Item1}' (plate {plate}) - skipping.", logFilePath);
                    uploadFail++;
                    continue;
                }
                ownerIdCache[ownerKey] = ownerId;
            }

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "vehicle");
            request.Headers.Add("Authorization", "token " + token);
            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("x-salt", "683239");
            request.Headers.Add("x-alpha", "21");
            var json = $@"{{""number_plate"": ""{plate}"", ""vehicle_type"": {info.Item4}, ""auth_type"": ""whitelist"", ""comment"": ""{info.Item1}"", ""owner"": ""{ownerId}""}}";
            request.Content = new StringContent(json, null, "application/json");
            var response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) uploadSuccess++; else uploadFail++;
            LogApiRequest($"Reconcile {camera.ANPRIP}: upload {plate} ({info.Item1}) HTTP {(int)response.StatusCode} | {result}", logFilePath);
        }

        LogApiRequest($"Reconcile {camera.ANPRIP}: DONE. Deleted {toDelete.Count}, Uploaded {uploadSuccess}/{toUpload.Count} (failed {uploadFail}). Target was {authorizedPlates.Count}.", logFilePath);
    }

    public static async Task<string> CallPostApiAsync(string url, string token, Vehicle vh)
    {
        Console.WriteLine(DateTime.Now.ToString() + ": CallPostApiAsync");

        try
        {
            //Vehicle vh = new Vehicle
            //{
            //    number_plate = "MH-19AJ1234",
            //    vehicle_type = "1",
            //    auth_type = "whitelist",
            //    comment = "",
            //    owner = "2"
            //};
            if (vh != null)
            {
                if (vh.number_plate != "")
                {

                    var json = vh.ToString();
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
                    request.Content = content;

                    // Console.WriteLine("Raw response: " + responseBody);
                    var response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    // read response
                    var responseBody = await response.Content.ReadAsStringAsync();
                    // parse JSON
                    using var doc = JsonDocument.Parse(responseBody);


                    return doc.ToString();
                    //await  response.Content.ReadAsStringAsync();
                }
            }
            return "Error:";

        }
        catch (Exception ex)
        {
            return $"Error calling {url}: {ex.Message}";
        }


        //for (int i = 0; i >= 100; i++)
        //{
        //    if (i % 5 == 0)
        //        Console.WriteLine(i.ToString() + " - " + DateTime.Now.ToString());

        //}
        //return $"Error calling {url}: ex.Message";

    }

    public static async Task AddToMasterListIfNotExistsAsync(string neighbourhood, string plate)
    {
        try
        {
            if (string.IsNullOrEmpty(neighbourhood) || string.IsNullOrEmpty(plate)) return;

            string cleanPlate = plate.Replace(" ", "").Trim().ToUpper().Replace("'", "''");
            string cleanNeighbourhood = neighbourhood.Trim().Replace("'", "''");

            var dataAccess = new ANPRDataAccess(connectionString);
            DataTable dt = dataAccess.GetDataTable(
                "SELECT COUNT(1) AS cnt FROM ANPR_MasterVehicleList WHERE Neighbourhood = '" + cleanNeighbourhood + "' AND number_plate = '" + cleanPlate + "'");

            int count = (dt != null && dt.Rows.Count > 0) ? Convert.ToInt32(dt.Rows[0]["cnt"]) : 0;
            if (count == 0)
            {
                dataAccess.GetDataTable(
                    "INSERT INTO ANPR_MasterVehicleList (Neighbourhood, number_plate, vehicle_type) VALUES ('" + cleanNeighbourhood + "', '" + cleanPlate + "', 0)");
                LogApiRequest($"MasterList: added '{cleanPlate}' to '{cleanNeighbourhood}'.", logFilePath);
            }
        }
        catch (Exception ex)
        {
            LogApiRequest($"MasterList: failed to add '{plate}' - {ex.Message}", logFilePath);
        }
    }
    public static async Task<string> CallPostApiListAsync(string url, string token, ANPR_Controller _camera)
    {

        // defaultconnection 🔧 Replace with your actual connection string
        //string connectionString = "Server=localhost;Database=YourDatabaseName;Trusted_Connection=True;";
        // 📄 Replace with your actual view name
        //string sql = "SELECT * FROM ANPR_AddVehicle";//   where cameraid="+_camera.ID;

        string sql = "SELECT * FROM ANPR_AddVehicle WHERE Neighbourhood = '" + _camera.ANPRLocation + "'";

        DataTable dataTable = new DataTable();
        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();

                connection.Open();

                using (SqlDataAdapter adapter = new SqlDataAdapter(sql, connection))
                {
                    adapter.Fill(dataTable); // Fetch records into DataTable
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        try
        {
            if (dataTable != null)
            {
                if (dataTable.Rows.Count > 0)
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine("Fetching token... " + authUrl);
                        token = await GetTokenAsync(authUrl, clientId, clientSecret);
                        //string Token = ""; Token = token;
                    }
                    int SuccessCount = 0; int ErrorCount = 0;
                    Console.WriteLine($"Total Records Fetched: {dataTable.Rows.Count}");
                    List<Vehicle> UploadedVehicles = new List<Vehicle>();

                    foreach (DataRow row in dataTable.Rows)
                    {
                        //var col2 = reader.GetValue(1);
                        //Console.WriteLine($"{col1} | {col2}");
                        string Error = "";
                        // 0- number_plate, 1- vehicle_type, 2- auth_type, 3-comment, 4-Owner id
                        var r = row[0];
                        if (r != null)
                        {
                            if (r != "")
                            {
                                Vehicle rqstvh = new Vehicle
                                {
                                    number_plate = row[0].ToString().Replace(" ", ""),
                                    vehicle_type = Convert.ToUInt16(row[1].ToString()),
                                    auth_type = "whitelist",
                                    comment = row[3].ToString(),
                                    owner = 1// Convert.ToInt32(1.ToString().Replace(" ", ""))
                                };
                                //owner=Convert.ToDouble(row[6].ToString())
                                try
                                {

                                    if (rqstvh != null)
                                    {
                                        if (rqstvh.number_plate != "")
                                        {
                                            if (string.IsNullOrEmpty(rqstvh.comment))
                                            {
                                                rqstvh.comment = "";
                                            }

                                            //2-comment this code

                                            var client = new HttpClient();
                                            var request = new HttpRequestMessage(HttpMethod.Post, url);
                                            request.Headers.Add("Authorization", "token " + token);
                                            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                                            request.Headers.Add("x-salt", "683239");
                                            request.Headers.Add("x-alpha", "21");

                                            var json = $@"
                                                {{
                                                  ""number_plate"": ""{rqstvh.number_plate}"",
                                                  ""vehicle_type"": {rqstvh.vehicle_type},
                                                  ""auth_type"": ""whitelist"",
                                                  ""comment"": ""{rqstvh.comment}"",
                                                  ""owner"": ""{rqstvh.owner}""
                                                }}";
                                            var content = new StringContent(json, null, "application/json");

                                            Console.WriteLine("Payload " + json);
                                            request.Content = content;
                                            var response = client.Send(request);

                                            string responseContent = await response.Content.ReadAsStringAsync();

                                            if (response.IsSuccessStatusCode)
                                            {
                                                Console.WriteLine("✅ Success: " + responseContent);
                                                Error = "";
                                                SuccessCount++;
                                                rqstvh.auth_type = "whitelist Success";
                                                rqstvh.comment = "No Error";
                                            }

                                            //if (response.IsSuccessStatusCode)
                                            //{
                                            //    Console.WriteLine("✅ Success: " + responseContent);
                                            //    Error = "";
                                            //    SuccessCount++;
                                            //    rqstvh.auth_type = "whitelist Success";
                                            //    rqstvh.comment = "No Error";

                                            //    // Naya vehicle master list mein bhi daalo taaki agla
                                            //    // reconcile cycle ise "extra" samajh ke delete na kare
                                            //    await AddToMasterListIfNotExistsAsync(_camera.ANPRLocation, rqstvh.number_plate);
                                            //}
                                            else
                                            {
                                                Console.WriteLine($"❌ Error {response.StatusCode}: {responseContent}");
                                                Error = $"❌ Error {response.StatusCode}: {responseContent}";
                                                ErrorCount++;


                                                rqstvh.auth_type = "whitelist Failed";
                                                rqstvh.comment = "Error :" + Error.ToString();

                                                if (Error.Contains("Duplicate number plate")) {
                                                    string erMsg = Error.ToString();
                                                    Console.WriteLine($"⚠️ Duplicate Entry: {erMsg}");
                                                    UploadedVehicles.Add(rqstvh);

                                                    AddVehicle vhl = new AddVehicle()
                                                    {
                                                        id= 0,
                                                        number_plate=rqstvh.number_plate,
                                                        owner=rqstvh.owner, comment=rqstvh.comment,
                                                        auth_type=rqstvh.auth_type, vehicle_type=rqstvh.vehicle_type
                                                    };
                                                    // Save in DB
                                                    Program p1 = new Program();
                                                    await p1. InsertOrUpdateANPRVehicleAsync(connectionString, vhl, Convert.ToInt16(_camera.ANPRTerminalID));

                                                }
                                            }

                                            AddVehicle vehicle = new AddVehicle()
                                            {
                                                id =0,
                                                number_plate = rqstvh.number_plate,
                                                owner = rqstvh.owner,
                                                comment = rqstvh.comment,
                                                auth_type = rqstvh.auth_type,
                                                vehicle_type = rqstvh.vehicle_type
                                            };
                                            // Save in DB
                                            Program p = new Program();
                                            await p.InsertOrUpdateANPRVehicleAsync(connectionString, vehicle, Convert.ToInt16(_camera.ANPRTerminalID));

                                            Console.WriteLine(rqstvh.number_plate.ToString() + "Vehicle Post  in : " + _camera.ANPRIP.ToString());
                                            UploadedVehicles.Add(rqstvh);
                                            

                                        }

                                    }
                                    else
                                    {
                                        Error = $"No vehicle fount to register {url}:";
                                    }


                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error calling {url}: {ex.Message}");
                                    return $"Error calling {url}: {ex.Message}";

                                }
                            }
                        }
                        //  Console.WriteLine(Error);

                    }

                    LogApiRequest($"Total Records Fetched: {dataTable.Rows.Count}/Success:{SuccessCount}|Error:{ErrorCount}", logFilePath);
                    Console.WriteLine($"Total Records Fetched: {dataTable.Rows.Count}/Success:{SuccessCount}|Error:{ErrorCount}");
                    foreach (Vehicle rw in UploadedVehicles)
                    {
                        ANPRSyncStatus anpr = new ANPRSyncStatus()
                        {
                            //id   cameraId createdOn lastUploadedOn lastDownLoadedOn
                            numberPlate = rw.number_plate,
                            cameraId = _camera.ID,
                            cameraLocation = _camera.ID,
                            lastUploadedOn = DateTime.Now,
                            lastDownLoadedOn = null
                        };
                        if (rw.auth_type.Contains("whitelist Success"))
                        {
                            var r = await InsertOrUpdateVehicleAsyncCameraStatus(connectionString, anpr, _camera.ID);
                        }

                    }
                }
                else
                {
                    Console.WriteLine("No records found.");
                    return $"Error  {url}: {"VehicleList not get"}";

                }
            }
            else
            {
                Console.WriteLine("No records found.");
                return $"Error  {url}: {"VehicleList not get"}";
            }

        }
        catch (SqlException ex)
        {
            Console.WriteLine($"SQL Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }
        return $"Error  {url}: {"VehicleList not get Unknown Error"}";
    }

    public static async Task<string> CallPostApiListAsync_test(string url, string token, ANPR_Controller _camera)
    {

        // defaultconnection 🔧 Replace with your actual connection string
        //string connectionString = "Server=localhost;Database=YourDatabaseName;Trusted_Connection=True;";
        // 📄 Replace with your actual view name
        string sql = "SELECT * FROM ANPR_AddVehicle";//   where cameraid="+_camera.ID;

        DataTable dataTable = new DataTable();
        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();

                connection.Open();

                using (SqlDataAdapter adapter = new SqlDataAdapter(sql, connection))
                {
                    adapter.Fill(dataTable); // Fetch records into DataTable
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        try
        {
            if (dataTable != null)
            {
                if (dataTable.Rows.Count > 0)
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine("Fetching token... " + authUrl);
                        token = "ABC";// await GetTokenAsync(authUrl, clientId, clientSecret);
                        //string Token = ""; Token = token;
                    }
                    int SuccessCount = 0; int ErrorCount = 0;
                    Console.WriteLine($"Total Records Fetched: {dataTable.Rows.Count}");
                    List<Vehicle> UploadedVehicles = new List<Vehicle>();

                    foreach (DataRow row in dataTable.Rows)
                    {
                        //var col2 = reader.GetValue(1);
                        //Console.WriteLine($"{col1} | {col2}");
                        string Error = "";
                        // 0- number_plate, 1- vehicle_type, 2- auth_type, 3-comment, 4-Owner id
                        var r = row[0];
                        if (r != null)
                        {
                            if (r != "")
                            {
                                Vehicle rqstvh = new Vehicle
                                {
                                    number_plate = row[0].ToString().Replace(" ", ""),
                                    vehicle_type = Convert.ToUInt16(row[1].ToString()),
                                    auth_type = "whitelist",
                                    comment = row[3].ToString(),
                                    owner = 1// Convert.ToInt32(1.ToString().Replace(" ", ""))
                                };
                                //owner=Convert.ToDouble(row[6].ToString())
                                try
                                {

                                    if (rqstvh != null)
                                    {
                                        if (rqstvh.number_plate != "")
                                        {
                                            if (string.IsNullOrEmpty(rqstvh.comment))
                                            {
                                                rqstvh.comment = "";
                                            }

                                            //2-comment this code

                                            var client = new HttpClient();
                                            var request = new HttpRequestMessage(HttpMethod.Post, url);
                                            request.Headers.Add("Authorization", "token " + token);
                                            request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                                            request.Headers.Add("x-salt", "683239");
                                            request.Headers.Add("x-alpha", "21");

                                            var json = $@"
                                                {{
                                                  ""number_plate"": ""{rqstvh.number_plate}"",
                                                  ""vehicle_type"": {rqstvh.vehicle_type},
                                                  ""auth_type"": ""whitelist"",
                                                  ""comment"": ""{rqstvh.comment}"",
                                                  ""owner"": ""{rqstvh.owner}""
                                                }}";
                                            var content = new StringContent(json, null, "application/json");

                                            Console.WriteLine("Payload " + json);
                                            request.Content = content;

                                            Random rand = new Random();
                                            HttpResponseMessage rw = new HttpResponseMessage()
                                            {
                                                Content = content,
                                                StatusCode = (rand.Next(70) > 50) ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                                            };
                                            var response = rw; //client.Send(request);

                                            string responseContent = "await response.Content.ReadAsStringAsync()";

                                            if (response.IsSuccessStatusCode)
                                            {
                                                Console.WriteLine("✅ Success: " + responseContent);
                                                Error = "";
                                                SuccessCount++;
                                                rqstvh.auth_type = "whitelist Success";
                                                rqstvh.comment = "No Error";
                                            }
                                            else
                                            {
                                                Console.WriteLine($"❌ Error {response.StatusCode}: {responseContent}");
                                                Error = $"❌ Error {response.StatusCode}: {responseContent}";
                                                ErrorCount++;


                                                rqstvh.auth_type = "whitelist Failed";
                                                rqstvh.comment = "Error :" + Error.ToString();
                                            }

                                            Console.WriteLine(rqstvh.number_plate.ToString() + "Vehicle Post  in : " + _camera.ANPRIP.ToString());
                                            UploadedVehicles.Add(rqstvh);

                                        }

                                    }
                                    else
                                    {
                                        Error = $"No vehicle fount to register {url}:";
                                    }


                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error calling {url}: {ex.Message}");
                                    return $"Error calling {url}: {ex.Message}";

                                }
                            }
                        }
                        //  Console.WriteLine(Error);

                    }

                    LogApiRequest($"Total Records Fetched: {dataTable.Rows.Count}/Success:{SuccessCount}|Error:{ErrorCount}", logFilePath);
                    Console.WriteLine($"Total Records Fetched: {dataTable.Rows.Count}/Success:{SuccessCount}|Error:{ErrorCount}");
                    foreach (Vehicle rw in UploadedVehicles)
                    {
                        ANPRSyncStatus anpr = new ANPRSyncStatus()
                        {
                            //id   cameraId createdOn lastUploadedOn lastDownLoadedOn
                            numberPlate = rw.number_plate,
                            cameraId = _camera.ID,
                            cameraLocation = _camera.ID,
                            lastUploadedOn = DateTime.Now,
                            lastDownLoadedOn = null
                        };
                        if (rw.auth_type.Contains("whitelist Success"))
                        {
                            var r = await InsertOrUpdateVehicleAsyncCameraStatus(connectionString, anpr, _camera.ID);
                        }

                    }
                }
                else
                {
                    Console.WriteLine("No records found.");
                    return $"Error  {url}: {"VehicleList not get"}";

                }
            }
            else
            {
                Console.WriteLine("No records found.");
                return $"Error  {url}: {"VehicleList not get"}";
            }

        }
        catch (SqlException ex)
        {
            Console.WriteLine($"SQL Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }
        return $"Error  {url}: {"VehicleList not get Unknown Error"}";
    }

    //public static async Task<string> GetTokenAsync(string authUrl, string clientId, string clientSecret)
    //{
    //    Console.WriteLine("GetTokenAsync Call");

    //    try
    //    {
    //        //using var client = new HttpClient
    //        //{
    //        //    Timeout = TimeSpan.FromSeconds(10)   // Increased timeout to 30 seconds
    //        //};
    //        // Corrected JSON body
    //        string jsonContent = $"{{\"username\": \"{clientId}\", \"password\": \"{clientSecret}\"}}";
    //        LogApiRequest($"Client : {jsonContent}", logFilePath);

    //        var client = new HttpClient();
    //        var request = new HttpRequestMessage(HttpMethod.Post, authUrl);


    public static async Task<string> GetTokenAsync(string authUrl, string clientId, string clientSecret)
    {
        Console.WriteLine("GetTokenAsync Call");

        try
        {
            string jsonContent = $"{{\"username\": \"{clientId}\", \"password\": \"{clientSecret}\"}}";
            LogApiRequest($"Client : {jsonContent}", logFilePath);

            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15); // camera busy/slow hone par 15 sec me fail ho, loop hang na ho
            var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
            request.Headers.Add("X-Cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("X-Salt", "683239");
            request.Headers.Add("X-Alpha", "21");

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            Console.WriteLine("Token content: " + content.ToString());
            request.Content = content;

            var response = client.Send(request);

            Console.WriteLine("request send..");
            //var response = await client.SendAsync(request);
            LogApiRequest($"Response received", logFilePath);

            response.EnsureSuccessStatusCode();
            LogApiRequest($"Response EnsureSuccessStatusCode received", logFilePath);

            // Read good response
            var responseBody = await response.Content.ReadAsStringAsync();
            //Console.WriteLine("Raw response: " + responseBody);

            //LogApiRequest($"Response received: {responseBody}", logFilePath);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {response.StatusCode}");
                Console.WriteLine($"Error Body: {responseBody}");
                return "";
            }

            // Try parsing the response
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                string token = tokenElement.GetString();
                Console.WriteLine("Token: " + token);
                return token;
            }
            else
            {
                Console.WriteLine("Error: Token field missing in response.");
                return "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return "";
        }
    }

    public static async Task<string> GetTokenAsync_xxx(string authUrl, string clientId, string clientSecret)
    {
        Console.WriteLine("GetTokenAsync Call");

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)   // global timeout
            };
            // Construct JSON body with string interpolation
            string jsonContent = $"{{\"username\": \"{clientId}\", \"password\": \"{clientId}\"}}";


            var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
            request.Headers.Add("X-Cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("X-Salt", "683239");
            request.Headers.Add("X-Alpha", "21");

            request.Content = new StringContent(jsonContent,
            Encoding.UTF8,
            "application/json"
            );

            Console.WriteLine("Sending request...");
            // Log an API request
            LogApiRequest($"token Requested...", logFilePath);

            // timeout-safe
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await client.SendAsync(request, cts.Token);

            // Log an API request
            LogApiRequest($"Response received : {response.Content.ToString()}", logFilePath);

            // Handle non-success status codes
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {response.StatusCode}");
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error Response Body: {errorBody}");
                return null;
            }


            // Read good response
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Raw response: " + responseBody);


            // Try parse token
            try
            {
                using var doc = JsonDocument.Parse(responseBody);

                if (!doc.RootElement.TryGetProperty("token", out var tokenElement))
                {
                    Console.WriteLine("Error: Token field missing in response.");
                    return null;
                }

                string token = tokenElement.GetString();
                Console.WriteLine("Token: " + token);
                // Log an API request
                LogApiRequest($"Token received : {token.ToString()}", logFilePath);

                return token;
            }
            catch (System.Text.Json.JsonException jsonEx)
            {
                Console.WriteLine("JSON Parsing Error: " + jsonEx.Message);
                return null;
            }
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Request timeout after 10 seconds.");
            return null;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine("HTTP Error: " + httpEx.Message);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unexpected Error: " + ex.Message);
            return null;
        }
        finally
        {
            Console.WriteLine("API request completed (success/error).");
        }
    }
    public static async Task<string> GetTokenAsync_old(string authUrl, string clientId, string clientSecret)
    {
        Console.WriteLine("GetTokenAsync Call");
        string token = null;

        //var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        //var response = await client.SendAsync(request, cts.Token);

        try
        {
            //var authLogin = new
            //{
            //    username = clientId,
            //    password = clientSecret
            //};

            //var client = new HttpClient();

            //"http://192.168.31.7/api/v1/sdk/login"
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
            request.Headers.Add("X-Cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("X-Salt", "683239");
            request.Headers.Add("X-Alpha", "21");
            //request.Headers.Add("Authorization", "{{token}}");
            var content = new StringContent("{\n    \"username\": \"admin\",\n    \"password\": \"Admin@1923\"\n}", null, "application/json");
            //var content = new StringContent("{\n    \"username\": \"{{clientId}}\",\n    \"password\": \"{{clientSecret}}\"\n}", null, "application/json");
            request.Content = content;

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Raw response: " + responseBody);

            using var doc = JsonDocument.Parse(responseBody);
            token = doc.RootElement.GetProperty("token").GetString();
            Console.WriteLine(token);
            return token;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine("HTTP error: " + httpEx.Message);
        }
        //catch (JsonException jsonEx)
        //{
        //    Console.WriteLine("JSON parsing error: " + jsonEx.Message);
        //}
        catch (Exception ex)
        {
            Console.WriteLine("Token error: " + ex.Message);
        }
        finally
        {
            Console.WriteLine("Token success/error: ");
        }

        return null;
    }
    public static async Task<string> GetTokenAsync_codeErr(string authUrl, string clientId, string clientSecret)
    {
        Console.WriteLine("GetTokenAsync  Call");
        string Token = null;
        try
        {
            var Authlogin = new User
            {
                username = clientId,
                password = clientSecret
            };

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
            request.Headers.Add("X-Cue", "34db55e07f7b39df480284397f7f42ec");
            request.Headers.Add("X-Salt", "683239");
            request.Headers.Add("X-Alpha", "21");

            var content = new StringContent("{\n    \"username\": \"{{clientId}}\",\n    \"password\": \"{{clientSecret}}\"\n}", Encoding.UTF8, "application/json");
            Console.WriteLine("Token content: " + content);
            request.Content = content;

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // read response
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Raw response: " + responseBody);

            // parse JSON
            using var doc = JsonDocument.Parse(responseBody);
            var token = doc.RootElement.GetProperty("token").GetString();
            Token = token.ToString();
            return Token;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message.ToString());
        }

        return null;
        //using var doc = JsonDocument.Parse(responseBody);
        //return doc.RootElement.GetProperty("token").GetString();
    }
    public static async Task<bool> InsertOrUpdateVehicleAsyncCameraStatus(string connectionString, ANPRSyncStatus _syncVeh, int cameraid = 1)
    {
        // Create a DbContext with the provided connection string
        //  Anpr_in _vehicle = new Vehicle();
        ANPRSyncStatus ANPRSyncStatus = new ANPRSyncStatus()
        {
            id = _syncVeh.id,
            numberPlate = _syncVeh.numberPlate.ToString(),
            cameraId = Convert.ToInt16(_syncVeh.cameraId),
            createdOn = Convert.ToDateTime(_syncVeh.createdOn),
            //lastUploadedOn = Convert.ToDateTime(_syncVeh.lastUploadedOn.ToString()) ,
            //lastDownLoadedOn = Convert.ToDateTime(_syncVeh.lastDownLoadedOn.ToString()) 
        };

        bool _syncUpload = true;
        if (_syncVeh.lastDownLoadedOn == null)
        {
            ANPRSyncStatus.lastUploadedOn = Convert.ToDateTime(_syncVeh.lastUploadedOn.ToString());
        }
        else
        {
            ANPRSyncStatus.lastDownLoadedOn = Convert.ToDateTime(_syncVeh.lastDownLoadedOn.ToString());
            _syncUpload = false;
        }


        _syncUpload = true;
        var dataAccess = new ANPRDataAccess(connectionString);
        await dataAccess.InsertOrUpdateVehicleAsyncCameraStatus(ANPRSyncStatus, _syncUpload);

        return true;
    }

    public async Task InsertOrUpdateANPRVehicleAsync(string connectionString, AddVehicle _veh, int cameraid = 1)
    {
        // Create a DbContext with the provided connection string
        Vehicle _vehicle = new Vehicle();
        if (_veh.number_plate == null)
            _veh.number_plate = "";
        ANPR_vehicle _anprVehicle = new ANPR_vehicle()
        {
            NumberPlate = _veh.number_plate.ToString() + "",
            VType = Convert.ToInt16(_veh.vehicle_type.ToString()),
            Auth = false,
            ClientName = string.IsNullOrEmpty(_veh.comment) ? "New Added" : _veh.comment,
            CameraId = cameraid,
            SourceId = Convert.ToInt32(_veh.id)
        };

        var dataAccess = new ANPRDataAccess(connectionString);
        await dataAccess.InsertOrUpdateANPRVehicleAsync(_anprVehicle);
    }

    public async Task InsertOrUpdateANPRVehicleEventsAsync(string connectionString, ANPR_VehicleInoutRegister_log _vehInOutLog)
    {
        // Create a DbContext with the provided connection string      
        var dataAccess = new ANPRDataAccess(connectionString);
        await dataAccess.InsertOrUpdateANPRVehicleEventsAsync(_vehInOutLog);

    }

    public async Task DeletedANPRVehiclesAsync(string connectionString, string DeletedSourceIds, int cameraid = 1)
    {
        // Create a DbContext with the provided connection string      
        var dataAccess = new ANPRDataAccess(connectionString);
        await dataAccess.Delete_ANPRVehicleAsync(DeletedSourceIds, cameraid);
    }

    #region Cameras
    public static async Task<List<ANPR_Controller>> GetCameraAsync()
    {
        Console.WriteLine("CallPostControllerApiAsync");
        List<ANPR_Controller> _Controllers = new List<ANPR_Controller>();

        try
        {

            // Create a DbContext with the provided connection string   
            _Controllers = (List<ANPR_Controller>)await GetANPRList();

        }
        catch (Exception)
        {

            throw;
        }

        return _Controllers;

    }
    public static async Task BackfillHistoricalDataAsync(DateTime fromDate, DateTime toDate)
    {
        List<ANPR_Controller> cameras = await GetCameraAsync();
        var dataAccess = new ANPRDataAccess(connectionString);

        foreach (var camera in cameras)
        {
            if (!IsPingSuccessful(camera.ANPRIP))
            {
                Console.WriteLine($"Skipping offline camera {camera.ANPRIP}");
                continue;
            }

            string authUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/login";
            string baseUrl = "http://" + camera.ANPRIP + "/api/v1/sdk/";
            string token = await GetTokenAsync(authUrl, clientId, clientSecret);
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine($"Token failed for {camera.ANPRIP}, skipping.");
                continue;
            }

            //old code untill 22/09.2026
            //string url = baseUrl + "events?timestamp__gte=" + fromDate.ToString("yyyy-MM-dd HH:mm:ss")
            //                      + "&timestamp__lte=" + toDate.ToString("yyyy-MM-dd HH:mm:ss");

            string url = baseUrl + "events?timestamp__gte=" + Uri.EscapeDataString(fromDate.ToString("yyyy-MM-dd HH:mm:ss"))
                      + "&timestamp__lte=" + Uri.EscapeDataString(toDate.ToString("yyyy-MM-dd HH:mm:ss"));
            int page = 0, checkedCount = 0, insertedCount = 0;

            bool cameraFailed = false;

            while (!string.IsNullOrEmpty(url))
            {
                page++;
                HttpResponseMessage response;
                try
                {
                    var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(15); // don't hang forever on a dead/unreachable camera
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Authorization", "Token " + token);
                    request.Headers.Add("x-cue", "34db55e07f7b39df480284397f7f42ec");
                    request.Headers.Add("x-salt", "683239");
                    request.Headers.Add("x-alpha", "21");

                    response = await httpClient.SendAsync(request);
                }
                catch (Exception netEx)
                {
                    Console.WriteLine($"Camera {camera.ANPRIP}: network error on page {page} ({netEx.Message}). Skipping this camera, moving to next.");
                    cameraFailed = true;
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Camera {camera.ANPRIP}: page {page} failed ({response.StatusCode}). Stopping - rerun later to continue.");
                    cameraFailed = true;
                    break;
                }

                string jsonString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonString);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("results", out JsonElement results))
                {
                    foreach (JsonElement item in results.EnumerateArray())
                    {
                        checkedCount++;
                        try
                        {
                            string uniqueId = item.GetProperty("unique_id").ToString();
                            string numberPlate = item.GetProperty("number_plate").GetString();
                            DateTime inOutTime = DateTime.Parse(
                                item.GetProperty("updated_at").ToString(),
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind);

                            var log = new ANPR_VehicleInoutRegister_log
                            {
                                unique_id = uniqueId,
                                CameraID = camera.ID,
                                CameraLocation = camera.ANPRLocation,
                                VehicleNumber = numberPlate ?? "",
                                OwnerID = null,
                                InOutFlag = camera.ANPR_Mode,
                                InOutTime = inOutTime
                            };

                            // Existing exists-check (CameraID + unique_id) makes this safe to insert - skips if already present.
                            await dataAccess.InsertOrUpdateANPRVehicleEventsAsync(log);
                            insertedCount++;
                        }
                        catch (Exception itemEx)
                        {
                            Console.WriteLine($"Bad item skipped: {itemEx.Message}");
                        }
                    }
                }

                url = (root.TryGetProperty("next", out JsonElement nextEl) && nextEl.ValueKind == JsonValueKind.String)
                    ? nextEl.GetString() : null;

                if (page % 20 == 0)
                    Console.WriteLine($"Camera {camera.ANPRIP}: page {page}, checked {checkedCount} so far...");
            }

            if (cameraFailed)
                Console.WriteLine($"Camera {camera.ANPRIP}: STOPPED EARLY due to error. Checked {checkedCount} events, {page} page(s). Rerun same command later to retry this camera.");
            else
                Console.WriteLine($"Camera {camera.ANPRIP}: DONE. Checked {checkedCount} events, {page} page(s).");
        }

        Console.WriteLine("Backfill complete for all cameras.");
    }

    #endregion


    // Method to log API request details

    //check ping  
    static bool IsPingSuccessful(string ipAddress)
    {
        using (Ping ping = new Ping())
        {
            PingReply reply = ping.Send(ipAddress, 1000);

            try
            {
                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"New=Network Error writing to log file: {ex.Message}");

            }
            return false;

        }
    }


}

public class ANPR_Controller
{
    public int ID { get; set; }
    public string ANPRTerminalID { get; set; }
    public string ANPRIP { get; set; }
    public string ANPRport { get; set; }
    public string ANPRLocation { get; set; }
    public bool? isActive { get; set; } = true;
    public bool? IsDeleted { get; set; } = false;
    public string? ANPR_Mode { get; set; } = "IN";     //In-[In Mode] | Out-[Out Mode]
    public DateTime? last_updatedOn { get; set; } = null;
    public DateTime? last_downloadedOn { get; set; } = null;
}

public class User
{
    public string? username { get; set; } = "";
    public string? password { get; set; } = "";
}

public class Vehicle
{
    [Required]
    public string? number_plate { get; set; } = "";
    [Required]
    public int? vehicle_type { get; set; } = 0;// "2-Wheeler"; // 4-Wheeler
    public string? auth_type { get; set; } = "whitelist";
    public string? comment { get; set; } = "";
    [Required]
    public double? owner { get; set; } = 0; // owner required

    // public int? id { get; set; } = 0; // owner required
    // number_plate , "vehicle_type"   ,"auth_type", "comment" ,"owner","id"

}

public class AddVehicle
{
    [Required]
    public string? number_plate { get; set; } = "";
    [Required]
    public int? vehicle_type { get; set; } = 0;// "2-Wheeler"; // 4-Wheeler
    public string? auth_type { get; set; } = "whitelist";
    public string? comment { get; set; } = "";
    [Required]
    public double? owner { get; set; } = 0; // owner required

    //public int? id { get; set; } = 0; // owner required
    //                                  // number_plate , "vehicle_type"   ,"auth_type", "comment" ,"owner","id"
    public int? id { get; set; } = 0;

     
}

public class DeleteVehicle
{
    public string? comment;
    public string? auth_type;    // "blacklist"
}


//Aditya-ANPR 8999918783
public class ANPR
{
    [Key]
    public int messageid { get; set; }
    public bool auth { get; set; } = false; //register vehicle if is 
    public string frame { get; set; } = null;
    public string image { get; set; } = null;
    public bool is_entry { get; set; } = false;
    public string number_plate { get; set; } = null;
    public DateTime timestamp { get; set; } = DateTime.Now;
    public string type { get; set; }
    public string client_name { get; set; } = null;
    public string site { get; set; } = null;
    public string site_name { get; set; } = null;
    public int camera_id { get; set; } = 0;
    public int brand { get; set; } = 0;
    public string client { get; set; }
    public int source_id { get; set; }
    public string node_name { get; set; } = null;
    public int node { get; set; } = 0;
    public int _class { get; set; } = 0;
    public int color { get; set; } = 0;
    public string hashed_number_plate { get; set; } = "";
    public int speed { get; set; } = 0;
    public string vehicle { get; set; } = null;
    public int vtype { get; set; } = 0; // vehicle type    
}

public class ANPR_VehicleNumberPlateUpdates
{
    //[Key]

    public string id { get; set; }
    public DateTime updated_at { get; set; }
    public string? number_plate { get; set; }               //"": ""MH12QH4388"",
    public bool auth { get; set; } = false;                //auth"": false,
    public DateTime created_at { get; set; }               // "": ""2025 - 11 - 15T16: 07:49.749637 + 05:30"",
    public bool is_entry { get; set; } = false;            //"": false;
    public int vehicle_type { get; set; }                  //"": 1,
    public string? unique_id { get; set; }                  //"": ""2025 - 11 - 15 - 16 - 07 - 49_4744"",
    public string plate_img_uri { get; set; } = null;       //"": null,
    public string frame_img_uri { get; set; } = null;       //"": null,
    public string? model { get; set; }                       //"": "" - 1"",
    public bool is_blacklisted { get; set; } = false;       //"": false,
                                                            // upload_status            
                                                            //"": {
                                                            //""event"": false,
                                                            //                ""frame"": false,
                                                            //                ""plate"": false
                                                            //}
}

[Table("tblANPR_VehicleInoutRegister")]
public class ANPR_VehicleInoutRegister_log
{
    //id CameraID, CameraLocation, CameraImage, CameraLocation, CameraNumberPlateImage, VehicleNumber, OwnerID, InOutFlag, InOutTime
    [Key]
    public int id { get; set; }

    public int CameraID { get; set; }
    public string CameraLocation { get; set; }

    public BinaryData? CameraImage { get; set; }
    public BinaryData? CameraNumberPlateImage { get; set; }
    public string VehicleNumber { get; set; }
    public string OwnerID { get; set; }
    public string InOutFlag { get; set; } = "IN";
    public DateTime? InOutTime { get; set; }
    public string? unique_id { get; set; } = "";

    public int? VehicleType { get; set; } = null;

}

