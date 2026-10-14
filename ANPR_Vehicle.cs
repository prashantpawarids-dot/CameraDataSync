
namespace ApiTaskApp
{
    using System;
    using static System.Net.Mime.MediaTypeNames;
    using System.Drawing;
    using System.Runtime.InteropServices.JavaScript;
    using System.Security.Claims;
    using System.Xml.Linq;
    using System.Data.SqlClient;
    using Newtonsoft.Json.Linq;
    using System.Data;
    using Microsoft.Identity.Client;
    using System.Diagnostics.Metrics;
    using System.Net.WebSockets;


    public class ANPR_vehicle
    {
        public bool Auth { get; set; } = false;
        public byte[]? Frame { get; set; } = null;
        public byte[]? Image { get; set; } = null;
        public bool IsEntry { get; set; } = true;
        public string NumberPlate { get; set; } = "";
        public string Type { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string Site { get; set; } = "1";
        public string SiteName { get; set; } = "RiverviewCity";
        public int CameraId { get; set; } = 1001;
        public int Brand { get; set; } = 1;
        public string Client { get; set; } = "Unknow";
        public int SourceId { get; set; } = 0;
        public string NodeName { get; set; } = "";
        public int Node { get; set; } = 0;
        public int Class { get; set; } = 0;
        public int Color { get; set; } = 0;
        public string HashedNumberPlate { get; set; } = "";
        public int Speed { get; set; } = 0;
        public string Vehicle { get; set; } = "Un-known";
        public int VType { get; set; } = 1;
    }
    public class ANPRSyncStatus
    {
        //id  numberPlate cameraId createdOn lastUploadedOn lastDownLoadedOn
        public int id { get; set; }
        public string numberPlate { get; set; }
        public int cameraId { get; set; }
        public int cameraLocation { get; set; }
        public DateTime? createdOn { get; set; }
        public DateTime? lastUploadedOn { get; set; }
        public DateTime? lastDownLoadedOn { get; set; }
    }
    public class ANPRDataAccess
    {
        private readonly string _connectionString;
        public int inserted = 0; public int updated = 0;
        public ANPRDataAccess(string connectionString)
        {
            _connectionString = connectionString;
            // _connectionString = "Data Source=DESKTOP-7I8JMHB\\SQLEXPRESS;Initial Catalog=IDS_ASSDB_21082025;User ID=sa;Password=Admin@123;Encrypt=False;TrustServerCertificate=false;";
        }

        #region common_Methods

        public DataTable GetDataTable(string query)
        {
            try
            {  
                using (var connection = new SqlConnection(_connectionString))
                {

                    DataTable dt = new DataTable();
                    SqlCommand cmd = new SqlCommand(query, connection);

                    using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                    {
                        connection.Open();
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
            catch (Exception ex)
            {
                var Err = ex.Message.ToString();
                Console.WriteLine(Err);
            }
            return null;
        }

        #endregion

        #region Delete_Vehicle 

        //bulk delete
        //public async Task Delete_ANPRVehicleAsync(string SourceID, int CameraID)
        //{
        //    using (var connection = new SqlConnection(_connectionString))
        //    {
        //        await connection.OpenAsync();

        //        // Check if the vehicle already exists (use either SourceId or NumberPlate to check)
        //        string checkQuery = @"
        //        SELECT COUNT(1) FROM [dbo].[tblANPR_vehicle] 
        //        WHERE [source_id] in( @) SourceId and   camera_id = @CameraID";

        //        using (var command = new SqlCommand(checkQuery, connection))
        //        {
        //            command.Parameters.AddWithValue("@SourceId", SourceID);
        //            command.Parameters.AddWithValue("@CameraID", CameraID);

        //            var exists = (int)await command.ExecuteScalarAsync() > 0;

        //            if (exists)
        //            {
        //                // Update existing vehicle record
        //                string DeleteQuery = @"
        //                Delete  from  tblANPR_vehicle 
        //                 WHERE [source_id] in(@SourceId)";

        //                using (var deleteCommand = new SqlCommand(DeleteQuery, connection))
        //                {
        //                    // Add parameters to the update query
        //                    DeleteParameters(deleteCommand, SourceID, CameraID);
        //                    await deleteCommand.ExecuteNonQueryAsync();
        //                }
        //            }
        //        }
        //    }
        //}



        public async Task Delete_ANPRVehicleAsync(string SourceID, int CameraID)
        {
            // SourceID = comma-separated camera-side vehicle ids, e.g. "101,205,309"
            if (string.IsNullOrWhiteSpace(SourceID)) return;

            var idList = SourceID.Split(',')
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();

            if (idList.Count == 0) return;

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var paramNames = idList.Select((id, idx) => "@id" + idx).ToList();
                string inClause = string.Join(",", paramNames);

                string deleteQuery = $@"
            DELETE FROM [dbo].[tblANPR_vehicle]
            WHERE camera_id = @CameraID
              AND source_id IN ({inClause})";

                using (var deleteCommand = new SqlCommand(deleteQuery, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@CameraID", CameraID);
                    for (int i = 0; i < idList.Count; i++)
                        deleteCommand.Parameters.AddWithValue(paramNames[i], idList[i]);

                    int rows = await deleteCommand.ExecuteNonQueryAsync();
                    Console.WriteLine($"Delete_ANPRVehicleAsync: removed {rows} local row(s) for camera {CameraID}.");
                }
            }
        }

        /*
         public async Task Get_ANPRCamerasAsync()
         {
             using (var connection = new SqlConnection(_connectionString))
             {
                 await connection.OpenAsync();

                 // Check if the vehicle already exists (use either SourceId or NumberPlate to check)
                 string checkQuery = @"SELECT COUNT(1) FROM [dbo].[vwANPRCameras] ";
                 using (var command = new SqlCommand(checkQuery, connection))
                 {                   
                     var exists = (int)await command.ExecuteScalarAsync() > 0;
                     if (exists)
                     {
                         // Update existing vehicle record
                         string DQuery = @"
                         Select *  from  vwANPRCameras";

                         using (var SelCommand = new SqlCommand(DQuery, connection))
                         {
                             // Add parameters to the update query 
                             await SelCommand.ExecuteNonQueryAsync();
                         }
                     }
                 }
             }
         }
        */

        // Helper method to add parameters to the SQL command 
        private void DeleteParameters(SqlCommand command, string SourceId, int CameraID)
        {
            // command.Parameters.AddWithValue("@NumberPlate", NumberPlate);
            command.Parameters.AddWithValue("@SourceId", SourceId);
            command.Parameters.AddWithValue("@CameraID", CameraID);

        }


        #endregion

        #region VehicleEvents
        // Helper method to add parameters to the SQL command   ANPR_vehicle


        // Old code 24 sep 2026 adding new vehicle type for unknown vehicle
        //private void AddUpdateParameters_VehicleAccessLog(SqlCommand cmd, ANPR_VehicleInoutRegister_log vehicleIOLog)
        //{
        //    BinaryData? base64Data;
        //    //            System.Data.SqlClient.SqlException: 'The parameterized query '(@CameraID int, @CameraLocation nvarchar(9),
        //    //            @CameraImage nvarchar' expects the parameter '@CameraImage', which was not supplied.'


        //    // Add parameters 
        //    cmd.Parameters.AddWithValue("@CameraID", vehicleIOLog.CameraID);
        //    cmd.Parameters.AddWithValue("@CameraLocation", vehicleIOLog.CameraLocation);
        //    //cmd.Parameters.AddWithValue("@CameraImage", null);    //convert Uri in base64 and Pass
        //    // base64Data = vehicleIOLog.CameraImage;
        //    cmd.Parameters.AddWithValue("@CameraImage", new byte[] { 0x00 });

        //    cmd.Parameters.AddWithValue("@VehicleNumber", vehicleIOLog.VehicleNumber.ToString());
        //    cmd.Parameters.AddWithValue("@OwnerID", string.IsNullOrEmpty(vehicleIOLog.OwnerID) ? "0" : vehicleIOLog.OwnerID.ToString());
        //    cmd.Parameters.AddWithValue("@InOutFlag", vehicleIOLog.InOutFlag.ToString());
        //    cmd.Parameters.AddWithValue("@InOutTime", vehicleIOLog.InOutTime);

        //    //cmd.Parameters.AddWithValue("@CameraNumberPlateImage", null);    //convert Uri in base64 and Pass
        //    //BinaryData? base64DataImg = null;
        //    //base64DataImg = vehicleIOLog.CameraNumberPlateImage; 
        //    cmd.Parameters.AddWithValue("@CameraNumberPlateImage", new byte[] { 0x00 });// vehicleIOLog.CameraNumberPlateImage);
        //    cmd.Parameters.AddWithValue("@unique_id",  vehicleIOLog.unique_id);// vehicleIOLog.CameraNumberPlateImage);
        //}


        //New code 24 sep 2026 adding new vehicle type for unknown vehicle
        private void AddUpdateParameters_VehicleAccessLog(SqlCommand cmd, ANPR_VehicleInoutRegister_log vehicleIOLog)
        {
            BinaryData? base64Data;

            // Add parameters 
            cmd.Parameters.AddWithValue("@CameraID", vehicleIOLog.CameraID);
            cmd.Parameters.AddWithValue("@CameraLocation", vehicleIOLog.CameraLocation);
            cmd.Parameters.AddWithValue("@CameraImage", new byte[] { 0x00 });

            cmd.Parameters.AddWithValue("@VehicleNumber", vehicleIOLog.VehicleNumber.ToString());
            cmd.Parameters.AddWithValue("@OwnerID", string.IsNullOrEmpty(vehicleIOLog.OwnerID) ? "0" : vehicleIOLog.OwnerID.ToString());
            cmd.Parameters.AddWithValue("@InOutFlag", vehicleIOLog.InOutFlag.ToString());
            cmd.Parameters.AddWithValue("@InOutTime", vehicleIOLog.InOutTime);

            cmd.Parameters.AddWithValue("@CameraNumberPlateImage", new byte[] { 0x00 });
            cmd.Parameters.AddWithValue("@unique_id", vehicleIOLog.unique_id);
            cmd.Parameters.AddWithValue("@VehicleType", (object?)vehicleIOLog.VehicleType ?? DBNull.Value);
        }



        public async Task InsertOrUpdateANPRVehicleEventsAsync(ANPR_VehicleInoutRegister_log vehicleAccessLog)
        {
            string _uniqueid = string.IsNullOrEmpty(vehicleAccessLog.unique_id) ? "" : vehicleAccessLog.unique_id.ToString();
            //string checkQuery = @"
            //        SELECT COUNT(1) as result FROM [dbo].[tblANPR_VehicleInoutRegister] 
            // WHERE (VehicleNumber = '" + vehicleAccessLog.VehicleNumber.ToString() + "'  and unique_id  = '" + _uniqueid.ToString() + "')";


            string checkQuery = @"
                    SELECT COUNT(1) as result FROM [dbo].[tblANPR_VehicleInoutRegister] 
             WHERE (CameraID = " + vehicleAccessLog.CameraID.ToString() + "  and unique_id  = '" + _uniqueid.ToString() + "')";

            //// WHERE (  [number_plate] = @NumberPlate   and Cast(InOutTime as DateTime) = Cast('"" + vehicleAccessLog.InOutTime.ToString() + ""' as DateTime))""";

            int count = 0;
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (SqlCommand cmd = new SqlCommand(checkQuery, conn))
                {
                    try
                    {
                        var resultR = cmd.ExecuteScalar();

                        count = (int)resultR;
                    }
                    catch (Exception Err)
                    {
                        var r = Err.InnerException;
                    }
                }
            }

            Console.WriteLine(count);

            bool exists = count > 0;
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                //if (exists)
                //{
                //    // Update existing vehicle record
                //    string updateQuery = @"
                //        UPDATE [dbo].[tblANPR_VehicleInoutRegister]
                //        SET [CameraID] =@CameraID ,[CameraLocation]=@CameraLocation  ,[CameraImage]=@CameraImage,[VehicleNumber] =@VehicleNumber  
                //        ,[OwnerID]=@OwnerID   ,[InOutFlag]=@InOutFlag   ,[InOutTime] =@InOutTime  ,[CameraNumberPlateImage]=@CameraNumberPlateImage ,unique_id=@unique_id
                //        WHERE (  [VehicleNumber] = @NumberPlate   and  unique_id = '" + vehicleAccessLog.unique_id.ToString() + "')";

                //    await conn.OpenAsync();

                //    using (var updateCommand = new SqlCommand(updateQuery, conn))
                //    {
                //        // Add parameters to the update query
                //        AddUpdateParameters_VehicleAccessLog(updateCommand, vehicleAccessLog);
                //        await updateCommand.ExecuteNonQueryAsync();
                //        updated = updated + 1;
                //    }

                //}
                //else
                if (!exists)

                {
                    // Insert new vehicle record old quary 24 sep 2026
                    //string insertQuery = @"INSERT INTO [dbo].[tblANPR_VehicleInoutRegister]
                    //                ([CameraID]  ,[CameraLocation]  ,[CameraImage]   ,[VehicleNumber]   ,[OwnerID]   ,[InOutFlag]   ,[InOutTime]   ,[CameraNumberPlateImage],unique_id)
                    //                VALUES
                    //                (@CameraID,  @CameraLocation, @CameraImage, @VehicleNumber, @OwnerID, @InOutFlag,  @InOutTime,  @CameraNumberPlateImage,@unique_id)";


                    //new quary 24 sep 2026


                    string insertQuery = @"INSERT INTO [dbo].[tblANPR_VehicleInoutRegister]
                ([CameraID]  ,[CameraLocation]  ,[CameraImage]   ,[VehicleNumber]   ,[OwnerID]   ,[InOutFlag]   ,[InOutTime]   ,[CameraNumberPlateImage],unique_id,VehicleType)
                VALUES
                (@CameraID,  @CameraLocation, @CameraImage, @VehicleNumber, @OwnerID, @InOutFlag,  @InOutTime,  @CameraNumberPlateImage,@unique_id,@VehicleType)";

                    await conn.OpenAsync();
                    try
                    {
                        using (var insertCommand = new SqlCommand(insertQuery, conn))
                        {
                            // Add parameters to the insert query
                            AddUpdateParameters_VehicleAccessLog(insertCommand, vehicleAccessLog);
                            // await insertCommand.ExecuteNonQueryAsync();

                            var rlt = insertCommand.ExecuteNonQuery();
                            inserted = inserted + 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        var r = ex.Message.ToString();
                    }
                }
                conn.Close();
            }
        }
        
        #endregion

        #region AddVehicle
        // Helper method to add parameters to the SQL command   ANPR_vehicle
        private void AddParameters(SqlCommand cmd, ANPR_vehicle vehicle)
        {

            // Add parameters
            cmd.Parameters.AddWithValue("@Auth", true);
            cmd.Parameters.AddWithValue("@Frame", new byte[] { 0x00 });  // binary(1)
            cmd.Parameters.AddWithValue("@Image", new byte[] { 0x00 });  // binary(1)
            cmd.Parameters.AddWithValue("@IsEntry", true);
            cmd.Parameters.AddWithValue("@NumberPlate", vehicle.NumberPlate);
            cmd.Parameters.AddWithValue("@Type", vehicle.Type);
            cmd.Parameters.AddWithValue("@ClientName", vehicle.ClientName);
            cmd.Parameters.AddWithValue("@Site", "Site001");
            cmd.Parameters.AddWithValue("@SiteName", "Main Gate");
            cmd.Parameters.AddWithValue("@CameraId", vehicle.CameraId);
            cmd.Parameters.AddWithValue("@Brand", 1);
            cmd.Parameters.AddWithValue("@Client", vehicle.Client);
            cmd.Parameters.AddWithValue("@SourceId", vehicle.SourceId);
            cmd.Parameters.AddWithValue("@NodeName", "Node-1");
            cmd.Parameters.AddWithValue("@Node", 1);
            cmd.Parameters.AddWithValue("@Class", 2);
            cmd.Parameters.AddWithValue("@Color", 3);
            cmd.Parameters.AddWithValue("@HashedNumberPlate", vehicle.HashedNumberPlate);
            cmd.Parameters.AddWithValue("@Speed", 60);
            cmd.Parameters.AddWithValue("@Vehicle", "Toyota Corolla");
            cmd.Parameters.AddWithValue("@Vtype", 1);

        }
        public async Task InsertOrUpdateANPRVehicleAsync(ANPR_vehicle vehicle)
        {
            try
            {
                //string connectionString = "Server=YOUR_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USERNAME;Password=YOUR_PASSWORD;";

                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    // Check if the vehicle already exists (use either SourceId or NumberPlate to check)

                    string checkQuery = @"
SELECT COUNT(1) 
FROM [dbo].[tblANPR_vehicle] 
WHERE [number_plate] ='" + vehicle.NumberPlate.ToString() + "' AND camera_id =" + vehicle.CameraId;

                    using (var command = new SqlCommand(checkQuery, connection))
                    {
                        //command.Parameters.AddWithValue("@SourceId", vehicle.SourceId);
                        //command.Parameters.AddWithValue("@NumberPlate", vehicle.NumberPlate);
                        //command.Parameters.AddWithValue("@CameraID", vehicle.CameraId);
                        bool exists = false;
                        var result = command.ExecuteScalarAsync();
                        if (result.Result != null)
                        {
                            if (result.Result.ToString() != "0") { exists = true; }
                            if (exists)
                            {
                                // Update existing vehicle record so source_id gets the real camera-side ID
                                string updateQuery = @"
                        UPDATE [dbo].[tblANPR_vehicle]
                        SET [source_id] = @SourceId
                        WHERE [number_plate] = @NumberPlate AND camera_id = @CameraId";

                                using (var updateCommand = new SqlCommand(updateQuery, connection))
                                {
                                    updateCommand.Parameters.AddWithValue("@SourceId", vehicle.SourceId);
                                    updateCommand.Parameters.AddWithValue("@NumberPlate", vehicle.NumberPlate);
                                    updateCommand.Parameters.AddWithValue("@CameraId", vehicle.CameraId);
                                    var result1 = updateCommand.ExecuteNonQueryAsync();
                                    if (result1.Result > 0)
                                    {
                                        updated = updated + 1;
                                    }
                                }
                            }
                            if (!exists)
                            {
                                // Insert new vehicle record
                                string insertQuery = @"
                        INSERT INTO [dbo].[tblANPR_vehicle]
                               ([auth], [frame], [image], [is_entry], [number_plate], [type], 
                                [client_name], [site], [site_name], [camera_id], [brand], 
                                [client], [source_id], [node_name], [node], [class], [color], 
                                [hashed_number_plate], [speed], [vehicle], [vtype])
                        VALUES
                               (@Auth, @Frame, @Image, @IsEntry, @NumberPlate, @Type, 
                                @ClientName, @Site, @SiteName, @CameraId, @Brand, 
                                @Client, @SourceId, @NodeName, @Node, @Class, @Color, 
                                @HashedNumberPlate, @Speed, @Vehicle, @VType)";
                                try
                                {
                                    using (var insertCommand = new SqlCommand(insertQuery, connection))
                                    {
                                        // Add parameters to the insert query
                                        AddParameters(insertCommand, vehicle);
                                        var result1 = insertCommand.ExecuteScalarAsync();
                                        if (result1.Result != null)
                                        {
                                            if (result1.Result.ToString() != "0")
                                            {
                                                //var result1 = await command.ExecuteScalarAsync();
                                                //bool exists1 = result != null;
                                                inserted = inserted + 1;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    var r = ex.Message.ToString();
                                }
                            }

                        } 
                    }
                }
            }
            catch (Exception ex)
            {

                var r = ex.ToString();
            }
        }


        #endregion AddVehicle

        #region AddSyncVehicle
        private void Add_syncVehParameters(SqlCommand cmd, ANPRSyncStatus _syncVeh)
        {

            // Add parameters
            // cmd.Parameters.AddWithValue("@id", true);
            cmd.Parameters.AddWithValue("@numberPlate", _syncVeh.numberPlate);
            cmd.Parameters.AddWithValue("@cameraId", _syncVeh.cameraId); //
            cmd.Parameters.AddWithValue("@createdOn", _syncVeh.createdOn);//
            cmd.Parameters.AddWithValue("@lastUploadedOn", _syncVeh.lastUploadedOn);//lastUploadedOn
            cmd.Parameters.AddWithValue("@lastDownLoadedOn", _syncVeh.lastDownLoadedOn);//lastDownLoadedOn 
        }
        public async Task<int> InsertOrUpdateVehicleAsyncCameraStatus(ANPRSyncStatus _syncVeh, bool _syncUpload = true)
        { 
                using (var connection = new SqlConnection(_connectionString))
                {
                      await connection.OpenAsync();

                    string checkQuery = @"
        SELECT COUNT(1) 
        FROM dbo.tblvehicleSyncStatus
        WHERE numberPlate = @numberPlate 
        AND cameraId = @cameraId";

                    using (var command = new SqlCommand(checkQuery, connection))
                    {
                        command.Parameters.AddWithValue("@numberPlate", _syncVeh.numberPlate);
                        command.Parameters.AddWithValue("@cameraId", _syncVeh.cameraId);

                        var exists =(int)  command.ExecuteScalar();
 
                        if (exists>0 )
                        {
                            string updateQuery;

                            if (_syncUpload)
                            {
                                updateQuery = @"
                    UPDATE dbo.tblvehicleSyncStatus
                    SET lastUploadedOn = GETDATE()
                    WHERE numberPlate = @numberPlate 
                    AND cameraId = @cameraId";
                            }
                            else
                            {
                                updateQuery = @"
                    UPDATE dbo.tblvehicleSyncStatus
                    SET lastDownLoadedOn = GETDATE()
                    WHERE numberPlate = @numberPlate 
                    AND cameraId = @cameraId";
                            }

                            using (var updateCommand = new SqlCommand(updateQuery, connection))
                            {
                                updateCommand.Parameters.AddWithValue("@numberPlate", _syncVeh.numberPlate);
                                updateCommand.Parameters.AddWithValue("@cameraId", _syncVeh.cameraId);

                                await updateCommand.ExecuteNonQueryAsync();
                                updated++;
                            }
                        }
                        else
                        {
                            string insertQuery;

                            if (_syncUpload)
                            {
                                insertQuery = @"
                    INSERT INTO dbo.tblvehicleSyncStatus
                    (numberPlate, cameraId, createdOn, lastUploadedOn, currentstatus)
                    VALUES
                    (@numberPlate, @cameraId, GETDATE(), GETDATE(), @status)";
                            }
                            else
                            {
                                insertQuery = @"
                    INSERT INTO dbo.tblvehicleSyncStatus
                    (numberPlate, cameraId, createdOn, lastDownLoadedOn, currentstatus)
                    VALUES
                    (@numberPlate, @cameraId, GETDATE(), GETDATE(), @status)";
                            }

                            using (var insertCommand = new SqlCommand(insertQuery, connection))
                            {
                                insertCommand.Parameters.AddWithValue("@numberPlate", _syncVeh.numberPlate);
                                insertCommand.Parameters.AddWithValue("@cameraId", _syncVeh.cameraId);
                                insertCommand.Parameters.AddWithValue("@status", "SUCCESS"); // or FAILED

                            // await insertCommand.ExecuteNonQueryAsync();
                            insertCommand.ExecuteNonQuery();

                            inserted++;
                            }
                        }
                    }
                }
            return 0;

        }
        //     public async Task InsertOrUpdateCameraStatusSync(ANPR_Controller camera, bool syncUpload = true)
        //     {
        //         try
        //         {
        //             string updateQuery = syncUpload
        //? @"UPDATE tblANPR_Controller SET last_updatedOn = @dt WHERE ID = @id"
        //: @"UPDATE tblANPR_Controller SET last_downloadedOn = @dt WHERE ID = @id";

        //             using (SqlConnection connection = new SqlConnection(_connectionString))
        //             {
        //                 await connection.OpenAsync();
        //                 using (var updateCommand = new SqlCommand(updateQuery, connection))
        //                 {
        //                     updateCommand.Parameters.AddWithValue("@id", camera.ID);
        //                     updateCommand.Parameters.AddWithValue("@dt", DateTime.Now);

        public async Task InsertOrUpdateCameraStatusSync(ANPR_Controller camera, bool syncUpload = true, DateTime? explicitTimestamp = null)
        {
            try
            {
                string updateQuery = syncUpload
   ? @"UPDATE tblANPR_Controller SET last_updatedOn = @dt WHERE ID = @id"
   : @"UPDATE tblANPR_Controller SET last_downloadedOn = @dt WHERE ID = @id";

                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var updateCommand = new SqlCommand(updateQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@id", camera.ID);
                        updateCommand.Parameters.AddWithValue("@dt", explicitTimestamp ?? DateTime.Now);

                        int r =   updateCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DB Error: " + ex.Message);
            }
        }
         public   DataTable CreateVehicleTable(List<Vehicle> Vehicles, int CameraId, int syncFlag =0)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("camera_id", typeof(int));
            dt.Columns.Add("number_plate", typeof(string));
            dt.Columns.Add("syncFlag", typeof(int));

            foreach (var d in Vehicles)
                dt.Rows.Add(CameraId, d.number_plate, syncFlag);

            return dt;
        }

        public void UpdateSyncFlagFromVehicles(List<Vehicle> _vehicleList,int _cameraID, string syncFlag="N" )
        { 
            string sql = @"
                        MERGE tblANPR_vehicle AS target
                        USING (SELECT @camera_id AS camera_id, @number_plate AS number_plate) AS source
                        ON target.camera_id = source.camera_id AND target.number_plate = source.number_plate

                        WHEN MATCHED THEN
                            UPDATE SET 
                                target.SyncFlag = 1,
                                target.LastSyncDate = GETDATE()

                        WHEN NOT MATCHED THEN
                            INSERT (messageid,	auth,	frame,	image,	is_entry,	number_plate,	
                    timestamp,	type,	client_name,	site,	site_name,	camera_id,	brand,	
                    client,	source_id,	node_name,	node,	class,	color,	hashed_number_plate,	
                    speed,	vehicle,	vtype,	SyncFlag,	LastSyncDate )
                            VALUES ( CONVERT(BIGINT, FORMAT(GETDATE(), 'MMddHHmmss')) ,	null,	null,	null,	1,	@number_plate,	
                    timestamp,	null,	null,	null,	null,	@camera_id,	null,	
                    null,	null,	null,	null,	null,	null,	null,	
                    0,	null,	null, 1, GETDATE());
                    ";

            int _sync = syncFlag == "Y" ? 1 : 0;
            using (SqlConnection con = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(sql, con))
            {
                var param = cmd.Parameters.AddWithValue("@CreateVehicleTable", CreateVehicleTable(_vehicleList,_cameraID,_sync ));
                param.SqlDbType = SqlDbType.Structured;
                param.TypeName = "VehicleTableType";

                con.Open();
                cmd.ExecuteNonQuery();
            }

        }

        public async Task InsertIntoDatabase(VehicleEvent ev, byte[] image)
        {
            try
            {

                int count = 0;
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string checkQuery = @"
                    SELECT COUNT(1) as result FROM   tblANPR_TRImages  
             WHERE (unique_id = '" + ev.UniqueId.ToString() + "'  and Imagetype  = '" + ev.ImageType.ToString() + "')";

                    using (SqlCommand cmd = new SqlCommand(checkQuery, conn))
                    {
                        try
                        {
                            var resultR = cmd.ExecuteScalar();

                            count = (int)resultR;
                        }
                        catch (Exception Err)
                        {
                            var r = Err.InnerException;
                        }
                    }
                }

                Console.WriteLine(count);

                bool exists = count > 0;
                if (!exists)
                {
                    string sql = @"INSERT INTO tblANPR_TRImages (unique_id,CameraId, ImageURL, Imagetype,Data, CreatedAt) VALUES (@uid,@CameraId, @imgUrl, @type,@img, @time)";
                    using (SqlConnection conn = new SqlConnection(_connectionString))
                    {

                        SqlCommand cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@uid", ev.UniqueId);
                        cmd.Parameters.AddWithValue("@CameraId", ev.CameraId);
                        cmd.Parameters.AddWithValue("@imgUrl", ev.ImageUrl);
                        cmd.Parameters.AddWithValue("@type", ev.ImageType);
                        cmd.Parameters.AddWithValue("@img", image);
                        cmd.Parameters.AddWithValue("@time", ev.Timestamp);
                        await conn.OpenAsync();
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                var r = ex.Message.ToString();
                Console.WriteLine($"Error in Img Insert IntoDatabase  for {ev.ImageUrl}: {ex.Message}");
            }
        }


        #endregion AddSyncVehicle

        #region ImagesUpdateClasses
        //Aditya-ANPR 8999918783 
        public class ImageUrl
        {
            public string uniqueID { get; set; }
            public string url { get; set; }
            public string type { get; set; }
        }

        public class imgRoot
        {
            public List<ImageUrl> image_urls { get; set; }
        }

        public class VehicleEvent
        {
            public string UniqueId { get; set; }
            public string PlateNumber { get; set; }
            public string CameraId { get; set; }
            public string ImageUrl { get; set; }
            public string authUrl { get; set; }
            public string ImageType { get; set; }
            public DateTime Timestamp { get; set; }
        }
        public class vehicleAccessImageUpdateList
        {
            public string unique_id { get; set; } = null;
            public int Cameraid { get; set; } = 0;
            public string CameraLocation { get; set; } = null;
            public string shortname { get; set; } = null;
            public string VehicleNumber { get; set; } = null;
            public string InoutFlag { get; set; } = null;
            public DateTime InoutTime { get; set; } = DateTime.Now;
        }
        #endregion
    }

}
