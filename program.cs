using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace HyundaiDiagnosticSuite
{
    #region Configuration & Constants

    /// <summary>
    /// Vehicle-specific configuration for Hyundai i30 2012 1.4L Gamma (G4FA)
    /// </summary>
    public static class VehicleConfig
    {
        // Vehicle Identification
        public const string VEHICLE_NAME = "Hyundai i30 FD";
        public const string MODEL_YEAR = "2007-2012";
        public const string ENGINE_CODE = "G4FA";           // 1.4L Gamma
        public const string ENGINE_DISPLACEMENT = "1396cc";
        public const int ENGINE_POWER_PS = 109;
        public const int ENGINE_POWER_KW = 80;
        public const string FUEL_TYPE = "Petrol/Benzin";
        
        // CAN Bus Configuration  
        public const uint CAN_BAUD_RATE = 500000;           // 500 kbps
        public const bool USE_29BIT_IDS = false;            // 11-bit standard IDs
        public const uint CAN_BAUD_RATE_BODY = 500000;      // Body CAN also 500k on i30
        
        // Standard OBD-II Addresses (ISO 15765-4)
        public const uint OBD_BROADCAST_ID = 0x7DF;         // Functional addressing
        public const uint OBD_RESPONSE_MIN = 0x7E8;         // Response range start
        public const uint OBD_RESPONSE_MAX = 0x7EF;         // Response range end
        
        // ECM - Engine Control Module (Kefico/Bosch ME17.9.11)
        public const uint ECM_REQUEST_ID = 0x7E0;
        public const uint ECM_RESPONSE_ID = 0x7E8;
        
        // TCM - Transmission Control Module (A6MF1 if auto)
        public const uint TCM_REQUEST_ID = 0x7E1;
        public const uint TCM_RESPONSE_ID = 0x7E9;

        // Hyundai-Specific ECU Addresses for i30 FD
        public static readonly Dictionary<string, EcuDefinition> EcuAddresses = new()
        {
            ["ECM"] = new EcuDefinition
            {
                Name = "Engine Control Module",
                Description = "Kefico ME17.9.11 / Bosch EDC17",
                RequestId = 0x7E0,
                ResponseId = 0x7E8,
                FunctionalId = 0x7DF,
                SupportsUds = true,
                SupportsKwp = true,
                SecurityLevel = 0x01,
                DiagSession = 0x03,
                Category = EcuCategory.Powertrain
            },
            ["TCM"] = new EcuDefinition
            {
                Name = "Transmission Control Module",
                Description = "A6MF1/A6GF1 Automatic Transmission",
                RequestId = 0x7E1,
                ResponseId = 0x7E9,
                FunctionalId = 0x7DF,
                SupportsUds = true,
                SupportsKwp = true,
                SecurityLevel = 0x01,
                Category = EcuCategory.Powertrain
            },
            ["ABS_ESP"] = new EcuDefinition
            {
                Name = "ABS/ESC Control Module",
                Description = "Electronic Stability Control",
                RequestId = 0x7D1,
                ResponseId = 0x7D9,
                SupportsUds = true,
                SecurityLevel = 0x01,
                Category = EcuCategory.Chassis
            },
            ["MDPS"] = new EcuDefinition
            {
                Name = "Motor Driven Power Steering",
                Description = "Electric Power Steering ECU",
                RequestId = 0x7D4,
                ResponseId = 0x7DC,
                SupportsUds = true,
                Category = EcuCategory.Chassis
            },
            ["SRS"] = new EcuDefinition
            {
                Name = "Supplemental Restraint System",
                Description = "Airbag Control Module",
                RequestId = 0x7D0,
                ResponseId = 0x7D8,
                SupportsUds = true,
                SecurityLevel = 0x03,  // Higher security for safety systems
                Category = EcuCategory.Safety
            },
            ["BCM"] = new EcuDefinition
            {
                Name = "Body Control Module",
                Description = "Lighting, wipers, windows, locks",
                RequestId = 0x7A0,
                ResponseId = 0x7A8,
                SupportsUds = true,
                Category = EcuCategory.Body
            },
            ["ICU"] = new EcuDefinition  // Instrument Cluster Unit
            {
                Name = "Instrument Cluster",
                Description = "Dashboard/Gauge Cluster",
                RequestId = 0x7C6,
                ResponseId = 0x7CE,
                SupportsUds = true,
                Category = EcuCategory.Body
            },
            ["FATC"] = new EcuDefinition
            {
                Name = "Climate Control",
                Description = "Full Auto Temperature Control",
                RequestId = 0x7B0,
                ResponseId = 0x7B8,
                SupportsUds = true,
                Category = EcuCategory.Body
            },
            ["TPMS"] = new EcuDefinition
            {
                Name = "Tire Pressure Monitoring",
                Description = "TPMS Control Module",
                RequestId = 0x7A6,
                ResponseId = 0x7AE,
                SupportsUds = true,
                Category = EcuCategory.Chassis
            },
            ["ACU"] = new EcuDefinition
            {
                Name = "Audio Control Unit",
                Description = "Radio/Infotainment",
                RequestId = 0x7C0,
                ResponseId = 0x7C8,
                SupportsUds = true,
                Category = EcuCategory.Body
            },
            ["LDWS_FCWS"] = new EcuDefinition
            {
                Name = "Camera System",
                Description = "Lane Departure/Forward Collision Warning",
                RequestId = 0x7B2,
                ResponseId = 0x7BA,
                SupportsUds = true,
                Category = EcuCategory.Safety
            },
            ["PBOX"] = new EcuDefinition
            {
                Name = "P-Box/Telematics",
                Description = "Telematics Control Unit",
                RequestId = 0x7D6,
                ResponseId = 0x7DE,
                SupportsUds = true,
                Category = EcuCategory.Body
            },
            ["SMK"] = new EcuDefinition
            {
                Name = "Smart Key Module",
                Description = "Keyless Entry/Start System",
                RequestId = 0x7A5,
                ResponseId = 0x7AD,
                SupportsUds = true,
                SecurityLevel = 0x11,  // Immobilizer security
                Category = EcuCategory.Security
            },
            ["EPS"] = new EcuDefinition
            {
                Name = "Electric Parking Brake",
                Description = "EPB Control Module (if equipped)",
                RequestId = 0x7D5,
                ResponseId = 0x7DD,
                SupportsUds = true,
                Category = EcuCategory.Chassis
            },
            ["CGW"] = new EcuDefinition
            {
                Name = "Central Gateway",
                Description = "Network Gateway Module",
                RequestId = 0x746,
                ResponseId = 0x74E,
                SupportsUds = true,
                Category = EcuCategory.Network
            }
        };

        // Timing Configuration (ISO 15765-2 / ISO 14229)
        public const int P2_CLIENT_MAX_MS = 50;             // Max time for response
        public const int P2_STAR_CLIENT_MAX_MS = 5000;      // Extended timing after NRC 78
        public const int TX_TIMEOUT_MS = 1000;
        public const int RX_TIMEOUT_MS = 2000;
        public const int EXTENDED_TIMEOUT_MS = 10000;       // For long operations
        public const int TESTER_PRESENT_INTERVAL_MS = 2000;
        public const int SECURITY_TIMING_MS = 10;           // Inter-byte security timing
        
        // ISO-TP Configuration
        public const byte ISO_TP_BLOCK_SIZE = 0x00;         // 0 = No block limit
        public const byte ISO_TP_ST_MIN = 0x0A;             // 10ms separation time
        
        // Filter mask for standard 11-bit CAN IDs
        public const uint FILTER_MASK_11BIT = 0x7FF;
        public const uint FILTER_MASK_29BIT = 0x1FFFFFFF;
    }

    public enum EcuCategory
    {
        Powertrain,
        Chassis,
        Body,
        Safety,
        Network,
        Security
    }

    public class EcuDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public uint RequestId { get; set; }
        public uint ResponseId { get; set; }
        public uint FunctionalId { get; set; } = 0x7DF;
        public bool SupportsUds { get; set; } = true;
        public bool SupportsKwp { get; set; } = false;
        public byte SecurityLevel { get; set; } = 0x01;
        public byte DiagSession { get; set; } = 0x01;
        public EcuCategory Category { get; set; }
        public bool IsPresent { get; set; } = false;
        public string PartNumber { get; set; }
        public string SoftwareVersion { get; set; }
        public string HardwareVersion { get; set; }
    }

    #endregion

    #region UDS Service Identifiers

    /// <summary>
    /// ISO 14229 UDS Service Identifiers
    /// </summary>
    public static class UDS
    {
        // Diagnostic Session Control
        public const byte DIAGNOSTIC_SESSION_CONTROL = 0x10;
        public const byte DSC_DEFAULT_SESSION = 0x01;
        public const byte DSC_PROGRAMMING_SESSION = 0x02;
        public const byte DSC_EXTENDED_SESSION = 0x03;
        public const byte DSC_SAFETY_SESSION = 0x04;        // Hyundai-specific
        public const byte DSC_EOL_SESSION = 0x60;           // End of Line
        public const byte DSC_DEVELOPMENT_SESSION = 0x71;   // Development mode
        
        // ECU Reset
        public const byte ECU_RESET = 0x11;
        public const byte RESET_HARD = 0x01;
        public const byte RESET_KEY_OFF_ON = 0x02;
        public const byte RESET_SOFT = 0x03;
        
        // Security Access
        public const byte SECURITY_ACCESS = 0x27;
        // Odd = request seed, Even = send key
        public const byte SA_REQUEST_SEED_L1 = 0x01;
        public const byte SA_SEND_KEY_L1 = 0x02;
        public const byte SA_REQUEST_SEED_L2 = 0x03;
        public const byte SA_SEND_KEY_L2 = 0x04;
        public const byte SA_REQUEST_SEED_L3 = 0x11;        // Programming level
        public const byte SA_SEND_KEY_L3 = 0x12;
        
        // Communication Control
        public const byte COMMUNICATION_CONTROL = 0x28;
        public const byte CC_ENABLE_RX_TX = 0x00;
        public const byte CC_ENABLE_RX_DISABLE_TX = 0x01;
        public const byte CC_DISABLE_RX_ENABLE_TX = 0x02;
        public const byte CC_DISABLE_RX_TX = 0x03;
        
        // Tester Present
        public const byte TESTER_PRESENT = 0x3E;
        public const byte TP_ZERO_SUBFUNCTION = 0x00;
        public const byte TP_SUPPRESS_RESPONSE = 0x80;
        
        // Control DTC Setting
        public const byte CONTROL_DTC_SETTING = 0x85;
        public const byte DTC_SETTING_ON = 0x01;
        public const byte DTC_SETTING_OFF = 0x02;
        
        // Read Data By Identifier
        public const byte READ_DATA_BY_ID = 0x22;
        
        // Read Memory By Address
        public const byte READ_MEMORY_BY_ADDRESS = 0x23;
        
        // Read Scaling Data By Identifier
        public const byte READ_SCALING_DATA = 0x24;
        
        // Security Data Transmission
        public const byte SECURITY_DATA_TRANSMISSION = 0x27;
        
        // Write Data By Identifier
        public const byte WRITE_DATA_BY_ID = 0x2E;
        
        // Dynamically Define Data Identifier
        public const byte DYNAMICALLY_DEFINE_DID = 0x2C;
        
        // Input Output Control By Identifier
        public const byte IO_CONTROL_BY_ID = 0x2F;
        public const byte IOC_RETURN_CONTROL = 0x00;
        public const byte IOC_RESET_TO_DEFAULT = 0x01;
        public const byte IOC_FREEZE_CURRENT = 0x02;
        public const byte IOC_SHORT_TERM_ADJ = 0x03;
        
        // Routine Control
        public const byte ROUTINE_CONTROL = 0x31;
        public const byte RC_START_ROUTINE = 0x01;
        public const byte RC_STOP_ROUTINE = 0x02;
        public const byte RC_REQUEST_RESULTS = 0x03;
        
        // Request Download/Upload
        public const byte REQUEST_DOWNLOAD = 0x34;
        public const byte REQUEST_UPLOAD = 0x35;
        public const byte TRANSFER_DATA = 0x36;
        public const byte REQUEST_TRANSFER_EXIT = 0x37;
        public const byte REQUEST_FILE_TRANSFER = 0x38;
        
        // Clear DTC
        public const byte CLEAR_DTC = 0x14;
        
        // Read DTC Information
        public const byte READ_DTC_INFO = 0x19;
        public const byte RDTC_REPORT_NUMBER = 0x01;
        public const byte RDTC_REPORT_BY_STATUS = 0x02;
        public const byte RDTC_REPORT_SNAPSHOT_ID = 0x03;
        public const byte RDTC_REPORT_SNAPSHOT_DATA = 0x04;
        public const byte RDTC_REPORT_STORED = 0x05;
        public const byte RDTC_REPORT_PENDING = 0x07;
        public const byte RDTC_REPORT_CONFIRMED = 0x08;
        public const byte RDTC_REPORT_PERMANENT = 0x09;
        public const byte RDTC_REPORT_SUPPORTED = 0x0A;
        public const byte RDTC_REPORT_EXTENDED = 0x06;
        public const byte RDTC_REPORT_SEVERITY = 0x08;
        
        // Negative Response
        public const byte NEGATIVE_RESPONSE = 0x7F;
        
        // Positive response offset
        public const byte POSITIVE_RESPONSE_OFFSET = 0x40;
    }

    /// <summary>
    /// UDS Negative Response Codes
    /// </summary>
    public static class NRC
    {
        public const byte GENERAL_REJECT = 0x10;
        public const byte SERVICE_NOT_SUPPORTED = 0x11;
        public const byte SUB_FUNCTION_NOT_SUPPORTED = 0x12;
        public const byte INCORRECT_MESSAGE_LENGTH = 0x13;
        public const byte RESPONSE_TOO_LONG = 0x14;
        public const byte BUSY_REPEAT_REQUEST = 0x21;
        public const byte CONDITIONS_NOT_CORRECT = 0x22;
        public const byte REQUEST_SEQUENCE_ERROR = 0x24;
        public const byte NO_RESPONSE_FROM_SUBNET = 0x25;
        public const byte FAILURE_PREVENTS_EXECUTION = 0x26;
        public const byte REQUEST_OUT_OF_RANGE = 0x31;
        public const byte SECURITY_ACCESS_DENIED = 0x33;
        public const byte INVALID_KEY = 0x35;
        public const byte EXCEEDED_NUMBER_OF_ATTEMPTS = 0x36;
        public const byte REQUIRED_TIME_DELAY_NOT_EXPIRED = 0x37;
        public const byte UPLOAD_DOWNLOAD_NOT_ACCEPTED = 0x70;
        public const byte TRANSFER_DATA_SUSPENDED = 0x71;
        public const byte GENERAL_PROGRAMMING_FAILURE = 0x72;
        public const byte WRONG_BLOCK_SEQUENCE_COUNTER = 0x73;
        public const byte RESPONSE_PENDING = 0x78;
        public const byte SUB_FUNCTION_NOT_SUPPORTED_ACTIVE = 0x7E;
        public const byte SERVICE_NOT_SUPPORTED_ACTIVE = 0x7F;
        
        private static readonly Dictionary<byte, string> Descriptions = new()
        {
            [GENERAL_REJECT] = "General reject - request not processed",
            [SERVICE_NOT_SUPPORTED] = "Service not supported by this ECU",
            [SUB_FUNCTION_NOT_SUPPORTED] = "Sub-function not supported",
            [INCORRECT_MESSAGE_LENGTH] = "Incorrect message length or format",
            [RESPONSE_TOO_LONG] = "Response too long for transport",
            [BUSY_REPEAT_REQUEST] = "ECU busy, please repeat request",
            [CONDITIONS_NOT_CORRECT] = "Conditions not correct (engine running, etc.)",
            [REQUEST_SEQUENCE_ERROR] = "Request sequence error",
            [NO_RESPONSE_FROM_SUBNET] = "No response from sub-network component",
            [FAILURE_PREVENTS_EXECUTION] = "Failure prevents execution",
            [REQUEST_OUT_OF_RANGE] = "Request out of range",
            [SECURITY_ACCESS_DENIED] = "Security access denied - unlock required",
            [INVALID_KEY] = "Invalid security key",
            [EXCEEDED_NUMBER_OF_ATTEMPTS] = "Exceeded security access attempts - ECU locked",
            [REQUIRED_TIME_DELAY_NOT_EXPIRED] = "Security delay timer not expired",
            [UPLOAD_DOWNLOAD_NOT_ACCEPTED] = "Upload/download not accepted",
            [TRANSFER_DATA_SUSPENDED] = "Data transfer suspended",
            [GENERAL_PROGRAMMING_FAILURE] = "General programming failure",
            [WRONG_BLOCK_SEQUENCE_COUNTER] = "Wrong block sequence counter",
            [RESPONSE_PENDING] = "Response pending - extended timing",
            [SUB_FUNCTION_NOT_SUPPORTED_ACTIVE] = "Sub-function not supported in active session",
            [SERVICE_NOT_SUPPORTED_ACTIVE] = "Service not supported in active session"
        };
        
        public static string GetDescription(byte nrc) =>
            Descriptions.TryGetValue(nrc, out var desc) ? desc : $"Unknown NRC 0x{nrc:X2}";
        
        public static bool RequiresSecurityAccess(byte nrc) =>
            nrc == SECURITY_ACCESS_DENIED;
            
        public static bool ShouldRetry(byte nrc) =>
            nrc == BUSY_REPEAT_REQUEST || nrc == RESPONSE_PENDING;
    }

    /// <summary>
    /// Hyundai/Kia specific Data Identifiers (DIDs)
    /// </summary>
    public static class HyundaiDID
    {
        // Standard ISO 14229 DIDs
        public const ushort VIN = 0xF190;
        public const ushort ECU_MANUFACTURING_DATE = 0xF18B;
        public const ushort ECU_SERIAL_NUMBER = 0xF18C;
        public const ushort VEHICLE_MANUFACTURER_SPARE_PART_NUMBER = 0xF187;
        public const ushort VEHICLE_MANUFACTURER_ECU_SOFTWARE_NUMBER = 0xF188;
        public const ushort VEHICLE_MANUFACTURER_ECU_SOFTWARE_VERSION = 0xF189;
        public const ushort SYSTEM_SUPPLIER_ID = 0xF18A;
        public const ushort ECU_HARDWARE_VERSION = 0xF191;
        public const ushort SYSTEM_NAME = 0xF197;
        public const ushort REPAIR_SHOP_CODE = 0xF198;
        public const ushort PROGRAMMING_DATE = 0xF199;
        public const ushort CALIBRATION_ID = 0xF1A0;
        
        // Hyundai-specific DIDs (GDS protocol)
        public const ushort HMC_VIN = 0xF100;               // Alternative VIN location
        public const ushort VEHICLE_CONFIG = 0xF101;
        public const ushort ENGINE_TYPE = 0xF102;
        public const ushort TRANSMISSION_TYPE = 0xF103;
        public const ushort PRODUCTION_DATE = 0xF104;
        public const ushort VARIANT_CODING = 0xF105;
        
        // ECM-specific DIDs for Gamma 1.4
        public const ushort ECM_ENGINE_SPEED = 0xF400;
        public const ushort ECM_VEHICLE_SPEED = 0xF401;
        public const ushort ECM_COOLANT_TEMP = 0xF402;
        public const ushort ECM_INTAKE_AIR_TEMP = 0xF403;
        public const ushort ECM_THROTTLE_POSITION = 0xF404;
        public const ushort ECM_ENGINE_LOAD = 0xF405;
        public const ushort ECM_FUEL_PRESSURE = 0xF406;
        public const ushort ECM_IGNITION_TIMING = 0xF407;
        public const ushort ECM_AFR_BANK1 = 0xF408;
        public const ushort ECM_FUEL_TRIM_SHORT = 0xF409;
        public const ushort ECM_FUEL_TRIM_LONG = 0xF40A;
        public const ushort ECM_MAF_SENSOR = 0xF40B;
        public const ushort ECM_CATALYST_TEMP = 0xF40C;
        public const ushort ECM_BATTERY_VOLTAGE = 0xF40D;
        public const ushort ECM_OIL_TEMP = 0xF40E;
        public const ushort ECM_LAMBDA_B1S1 = 0xF410;
        public const ushort ECM_LAMBDA_B1S2 = 0xF411;
        public const ushort ECM_MISFIRE_COUNT = 0xF420;
        public const ushort ECM_KNOCK_COUNT = 0xF421;
        
        // ABS/ESP DIDs
        public const ushort ABS_WHEEL_SPEED_FL = 0xF500;
        public const ushort ABS_WHEEL_SPEED_FR = 0xF501;
        public const ushort ABS_WHEEL_SPEED_RL = 0xF502;
        public const ushort ABS_WHEEL_SPEED_RR = 0xF503;
        public const ushort ABS_BRAKE_PRESSURE = 0xF504;
        public const ushort ESP_LATERAL_ACCEL = 0xF510;
        public const ushort ESP_YAW_RATE = 0xF511;
        public const ushort ESP_STEERING_ANGLE = 0xF512;
        
        // MDPS DIDs
        public const ushort MDPS_STEERING_ANGLE = 0xF600;
        public const ushort MDPS_STEERING_TORQUE = 0xF601;
        public const ushort MDPS_MOTOR_CURRENT = 0xF602;
        public const ushort MDPS_ECU_TEMP = 0xF603;
        
        // SRS DIDs  
        public const ushort SRS_CRASH_DATA = 0xF700;
        public const ushort SRS_DEPLOYMENT_STATUS = 0xF701;
        
        // BCM DIDs
        public const ushort BCM_DOOR_STATUS = 0xF800;
        public const ushort BCM_LIGHT_STATUS = 0xF801;
        public const ushort BCM_WIPER_STATUS = 0xF802;
        
        // Cluster DIDs
        public const ushort CLU_ODOMETER = 0xF900;
        public const ushort CLU_FUEL_LEVEL = 0xF901;
        public const ushort CLU_WARNING_LAMPS = 0xF902;
    }

    /// <summary>
    /// Hyundai Routine Identifiers (RIDs) for service functions
    /// </summary>
    public static class HyundaiRID
    {
        // ECM Routines
        public const ushort ECM_INJECTOR_TEST = 0xF000;
        public const ushort ECM_IGNITION_TEST = 0xF001;
        public const ushort ECM_IDLE_LEARN = 0xF002;
        public const ushort ECM_THROTTLE_LEARN = 0xF003;
        public const ushort ECM_O2_SENSOR_TEST = 0xF004;
        public const ushort ECM_EVAP_TEST = 0xF005;
        public const ushort ECM_CATALYST_TEST = 0xF006;
        
        // Service Reset Routines
        public const ushort SERVICE_RESET_OIL = 0xFF00;
        public const ushort SERVICE_RESET_BRAKE = 0xFF01;
        public const ushort SERVICE_RESET_FILTER = 0xFF02;
        public const ushort SERVICE_RESET_BATTERY = 0xFF03;
        
        // Calibration Routines
        public const ushort SAS_CALIBRATION = 0xDF00;       // Steering Angle Sensor
        public const ushort TPMS_CALIBRATION = 0xDF01;      // Tire Pressure
        public const ushort EPB_CALIBRATION = 0xDF02;       // Electric Parking Brake
        public const ushort AFS_CALIBRATION = 0xDF03;       // Adaptive Front Lighting
        
        // ABS/ESP Routines
        public const ushort ABS_BLEED = 0xE000;
        public const ushort ESP_CALIBRATION = 0xE001;
        
        // BCM Routines
        public const ushort BCM_KEY_LEARN = 0xE100;
        public const ushort BCM_WINDOW_INIT = 0xE101;
        public const ushort BCM_SUNROOF_INIT = 0xE102;
        
        // SRS Routines
        public const ushort SRS_CRASH_RESET = 0xE200;       // After airbag deployment
    }

    #endregion

    #region Enhanced Logging System

    public enum LogLevel { Trace, Debug, Info, Warning, Error, Critical }

    public enum LogCategory
    {
        General,
        J2534,
        Protocol,
        Diagnostic,
        Security,
        Data,
        Error
    }

    /// <summary>
    /// Thread-safe async logging with rotation, filtering, and export
    /// </summary>
    public sealed class Logger : IDisposable
    {
        private static readonly Lazy<Logger> _lazy = 
            new(() => new Logger(), LazyThreadSafetyMode.ExecutionAndPublication);
        
        public static Logger Instance => _lazy.Value;

        private readonly object _writeLock = new();
        private readonly StreamWriter _fileWriter;
        private readonly StreamWriter _canTraceWriter;
        private readonly string _logPath;
        private readonly string _canTracePath;
        private readonly BlockingCollection<LogEntry> _queue;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _writerTask;
        private readonly Stopwatch _sessionTimer = Stopwatch.StartNew();
        
        private LogLevel _minLevel = LogLevel.Debug;
        private bool _consoleOutput = true;
        private bool _canTracing = false;
        private bool _disposed;

        public string LogFilePath => _logPath;
        public TimeSpan SessionTime => _sessionTimer.Elapsed;

        private class LogEntry
        {
            public DateTime Timestamp;
            public LogLevel Level;
            public LogCategory Category;
            public string Source;
            public string Message;
            public Exception Exception;
        }

        private Logger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(appData, "HyundaiDiagnosticSuite", "Logs");
            string canDir = Path.Combine(appData, "HyundaiDiagnosticSuite", "CANTrace");
            
            Directory.CreateDirectory(logDir);
            Directory.CreateDirectory(canDir);

            RotateLogs(logDir, 30);
            RotateLogs(canDir, 10);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logPath = Path.Combine(logDir, $"session_{timestamp}.log");
            _canTracePath = Path.Combine(canDir, $"can_{timestamp}.asc");

            _fileWriter = new StreamWriter(_logPath, false, Encoding.UTF8) { AutoFlush = false };
            _canTraceWriter = new StreamWriter(_canTracePath, false, Encoding.UTF8) { AutoFlush = false };
            
            // Write headers
            WriteHeader();
            WriteCanTraceHeader();

            _queue = new BlockingCollection<LogEntry>(10000);
            _writerTask = Task.Factory.StartNew(ProcessQueue, _cts.Token, 
                TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Log(LogLevel.Info, LogCategory.General, "Logger", 
                $"=== Session Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Log(LogLevel.Info, LogCategory.General, "Logger", 
                $"OS: {Environment.OSVersion}, .NET: {Environment.Version}, Arch: {(Environment.Is64BitProcess ? "x64" : "x86")}");
            Log(LogLevel.Info, LogCategory.General, "Logger", 
                $"Target: {VehicleConfig.VEHICLE_NAME} {VehicleConfig.ENGINE_CODE} {VehicleConfig.FUEL_TYPE}");
        }

        private void WriteHeader()
        {
            _fileWriter.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            _fileWriter.WriteLine("║           HYUNDAI i30 DIAGNOSTIC SUITE - SESSION LOG                         ║");
            _fileWriter.WriteLine($"║  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}                                               ║");
            _fileWriter.WriteLine($"║  Vehicle: {VehicleConfig.VEHICLE_NAME} {VehicleConfig.ENGINE_CODE}                                      ║");
            _fileWriter.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            _fileWriter.WriteLine();
        }

        private void WriteCanTraceHeader()
        {
            // ASC format header (compatible with Vector CANalyzer)
            _canTraceWriter.WriteLine("date " + DateTime.Now.ToString("ddd MMM dd hh:mm:ss tt yyyy", CultureInfo.InvariantCulture));
            _canTraceWriter.WriteLine("base hex  timestamps absolute");
            _canTraceWriter.WriteLine("internal events logged");
            _canTraceWriter.WriteLine("Begin Triggerblock");
        }

        private void RotateLogs(string dir, int keepDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        private void ProcessQueue()
        {
            var sb = new StringBuilder(4096);
            int batchSize = 0;
            
            while (!_cts.Token.IsCancellationRequested || _queue.Count > 0)
            {
                try
                {
                    if (_queue.TryTake(out var entry, 100, _cts.Token))
                    {
                        FormatLogEntry(sb, entry);
                        batchSize++;
                        
                        // Batch writes for performance
                        while (batchSize < 50 && _queue.TryTake(out entry, 0))
                        {
                            FormatLogEntry(sb, entry);
                            batchSize++;
                        }
                        
                        lock (_writeLock)
                        {
                            _fileWriter.Write(sb.ToString());
                            if (batchSize > 10)
                                _fileWriter.Flush();
                        }
                        
                        sb.Clear();
                        batchSize = 0;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private void FormatLogEntry(StringBuilder sb, LogEntry entry)
        {
            string level = entry.Level switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Info => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???"
            };

            sb.AppendFormat("[{0:HH:mm:ss.fff}] [{1}] [{2,-10}] [{3,-12}] {4}",
                entry.Timestamp, level, entry.Category, entry.Source, entry.Message);
            sb.AppendLine();

            if (entry.Exception != null)
            {
                sb.AppendLine($"  Exception: {entry.Exception.GetType().Name}: {entry.Exception.Message}");
                if (entry.Level >= LogLevel.Error && entry.Exception.StackTrace != null)
                {
                    foreach (var line in entry.Exception.StackTrace.Split('\n').Take(10))
                        sb.AppendLine($"    {line.Trim()}");
                }
            }
        }

        public void SetLevel(LogLevel level) => _minLevel = level;
        public void SetConsoleOutput(bool enabled) => _consoleOutput = enabled;
        public void SetCanTracing(bool enabled) => _canTracing = enabled;

        public void Log(LogLevel level, LogCategory category, string source, string message, Exception ex = null)
        {
            if (level < _minLevel || _disposed) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Source = source,
                Message = message,
                Exception = ex
            };

            _queue.TryAdd(entry);

            if (_consoleOutput)
            {
                WriteToConsole(entry);
            }
        }

        private void WriteToConsole(LogEntry entry)
        {
            var color = entry.Level switch
            {
                LogLevel.Trace => ConsoleColor.DarkGray,
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.Magenta,
                _ => ConsoleColor.White
            };

            lock (_writeLock)
            {
                var orig = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine($"[{entry.Level,-5}] [{entry.Source,-10}] {entry.Message}");
                Console.ForegroundColor = orig;
            }
        }

        /// <summary>
        /// Log CAN message in ASC format for analysis tools
        /// </summary>
        public void LogCanMessage(double timestamp, uint id, byte[] data, bool isTx)
        {
            if (!_canTracing || _disposed) return;

            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, 
                "  {0,12:F6} 1  {1:X3}             {2}x   d {3}",
                timestamp, id, isTx ? "T" : "R", data.Length);
            
            foreach (var b in data)
                sb.AppendFormat(" {0:X2}", b);

            lock (_writeLock)
            {
                _canTraceWriter.WriteLine(sb.ToString());
            }
        }

        // Convenience methods
        public void Trace(string source, string msg) => 
            Log(LogLevel.Trace, LogCategory.General, source, msg);
        public void Debug(string source, string msg) => 
            Log(LogLevel.Debug, LogCategory.General, source, msg);
        public void Info(string source, string msg) => 
            Log(LogLevel.Info, LogCategory.General, source, msg);
        public void Warn(string source, string msg) => 
            Log(LogLevel.Warning, LogCategory.General, source, msg);
        public void Error(string source, string msg, Exception ex = null) => 
            Log(LogLevel.Error, LogCategory.Error, source, msg, ex);
        public void Critical(string source, string msg, Exception ex = null) => 
            Log(LogLevel.Critical, LogCategory.Error, source, msg, ex);

        public void Protocol(string source, string msg) =>
            Log(LogLevel.Debug, LogCategory.Protocol, source, msg);
        public void Security(string source, string msg) =>
            Log(LogLevel.Info, LogCategory.Security, source, msg);
        public void Diag(string source, string msg) =>
            Log(LogLevel.Info, LogCategory.Diagnostic, source, msg);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Log(LogLevel.Info, LogCategory.General, "Logger", 
                $"=== Session End: Duration {SessionTime:hh\\:mm\\:ss} ===");

            _cts.Cancel();
            
            try { _writerTask.Wait(2000); } catch { }
            
            lock (_writeLock)
            {
                _fileWriter.Flush();
                _fileWriter.Dispose();
                
                _canTraceWriter.WriteLine("End TriggerBlock");
                _canTraceWriter.Flush();
                _canTraceWriter.Dispose();
            }
            
            _queue.Dispose();
        }
    }

    #endregion

    #region J2534 Definitions (Enhanced)

    public static class J2534Const
    {
        // Protocol IDs
        public const uint J1850VPW = 1;
        public const uint J1850PWM = 2;
        public const uint ISO9141 = 3;
        public const uint ISO14230 = 4;    // KWP2000
        public const uint CAN = 5;
        public const uint ISO15765 = 6;    // CAN with ISO-TP
        public const uint SCI_A_ENGINE = 7;
        public const uint SCI_A_TRANS = 8;
        public const uint SCI_B_ENGINE = 9;
        public const uint SCI_B_TRANS = 10;
        public const uint J1939 = 11;      // J2534-2
        public const uint J1708 = 12;      // J2534-2
        public const uint ANALOG_IN = 0x8001;
        public const uint ANALOG_OUT = 0x8002;

        // Connect Flags
        public const uint CAN_29BIT_ID = 0x0100;
        public const uint ISO9141_NO_CHECKSUM = 0x0200;
        public const uint CAN_ID_BOTH = 0x0800;
        public const uint ISO9141_K_LINE_ONLY = 0x1000;

        // Filter Types
        public const uint PASS_FILTER = 0x01;
        public const uint BLOCK_FILTER = 0x02;
        public const uint FLOW_CONTROL_FILTER = 0x03;

        // IOCTL IDs
        public const uint GET_CONFIG = 0x01;
        public const uint SET_CONFIG = 0x02;
        public const uint READ_VBATT = 0x03;
        public const uint FIVE_BAUD_INIT = 0x04;
        public const uint FAST_INIT = 0x05;
        public const uint CLEAR_TX_BUFFER = 0x07;
        public const uint CLEAR_RX_BUFFER = 0x08;
        public const uint CLEAR_PERIODIC_MSGS = 0x09;
        public const uint CLEAR_MSG_FILTERS = 0x0A;
        public const uint CLEAR_FUNCT_MSG_LOOKUP_TABLE = 0x0B;
        public const uint ADD_TO_FUNCT_MSG_LOOKUP_TABLE = 0x0C;
        public const uint DELETE_FROM_FUNCT_MSG_LOOKUP_TABLE = 0x0D;
        public const uint READ_PROG_VOLTAGE = 0x0E;
        public const uint SW_CAN_HS = 0x8000;
        public const uint SW_CAN_NS = 0x8001;

        // Config Parameter IDs
        public const uint DATA_RATE = 0x01;
        public const uint LOOPBACK = 0x03;
        public const uint NODE_ADDRESS = 0x04;
        public const uint NETWORK_LINE = 0x05;
        public const uint P1_MIN = 0x06;
        public const uint P1_MAX = 0x07;
        public const uint P2_MIN = 0x08;
        public const uint P2_MAX = 0x09;
        public const uint P3_MIN = 0x0A;
        public const uint P3_MAX = 0x0B;
        public const uint P4_MIN = 0x0C;
        public const uint P4_MAX = 0x0D;
        public const uint W0 = 0x19;
        public const uint W1 = 0x0E;
        public const uint W2 = 0x0F;
        public const uint W3 = 0x10;
        public const uint W4 = 0x11;
        public const uint W5 = 0x12;
        public const uint TIDLE = 0x13;
        public const uint TINIL = 0x14;
        public const uint TWUP = 0x15;
        public const uint PARITY = 0x16;
        public const uint BIT_SAMPLE_POINT = 0x17;
        public const uint SYNC_JUMP_WIDTH = 0x18;
        public const uint T1_MAX = 0x1A;
        public const uint T2_MAX = 0x1B;
        public const uint T3_MAX = 0x1C;
        public const uint T4_MAX = 0x1D;
        public const uint T5_MAX = 0x1E;
        public const uint ISO15765_BS = 0x1E;
        public const uint ISO15765_STMIN = 0x1F;
        public const uint ISO15765_BS_TX = 0x20;
        public const uint ISO15765_STMIN_TX = 0x21;
        public const uint DATA_BITS = 0x22;
        public const uint FIVE_BAUD_MOD = 0x23;
        public const uint ISO15765_WFT_MAX = 0x24;
        public const uint CAN_MIXED_FORMAT = 0x8000;
        public const uint J1962_PINS = 0x8001;

        // TxFlags
        public const uint ISO15765_FRAME_PAD = 0x0040;
        public const uint TX_NORMAL_TRANSMIT = 0x0000;
        public const uint WAIT_P3_MIN_ONLY = 0x0200;
        public const uint SCI_MODE = 0x400000;
        public const uint SCI_TX_VOLTAGE = 0x800000;
        public const uint CAN_29BIT_TX = 0x0100;
        public const uint ISO15765_ADDR_TYPE = 0x0080;
        public const uint ISO15765_EXT_ADDR = 0x0080;

        // RxStatus
        public const uint TX_MSG_TYPE = 0x0001;
        public const uint START_OF_MESSAGE = 0x0002;
        public const uint ISO15765_FIRST_FRAME = 0x0002;
        public const uint RX_BREAK = 0x0004;
        public const uint TX_DONE = 0x0008;
        public const uint ISO15765_PADDING_ERROR = 0x0010;
        public const uint ISO15765_ADDR_TYPE_RX = 0x0080;
        public const uint CAN_29BIT_RX = 0x0100;
    }

    public static class J2534Error
    {
        public const uint STATUS_NOERROR = 0x00;
        public const uint ERR_NOT_SUPPORTED = 0x01;
        public const uint ERR_INVALID_CHANNEL_ID = 0x02;
        public const uint ERR_INVALID_PROTOCOL_ID = 0x03;
        public const uint ERR_NULL_PARAMETER = 0x04;
        public const uint ERR_INVALID_IOCTL_VALUE = 0x05;
        public const uint ERR_INVALID_FLAGS = 0x06;
        public const uint ERR_FAILED = 0x07;
        public const uint ERR_DEVICE_NOT_CONNECTED = 0x08;
        public const uint ERR_TIMEOUT = 0x09;
        public const uint ERR_INVALID_MSG = 0x0A;
        public const uint ERR_INVALID_TIME_INTERVAL = 0x0B;
        public const uint ERR_EXCEEDED_LIMIT = 0x0C;
        public const uint ERR_INVALID_MSG_ID = 0x0D;
        public const uint ERR_DEVICE_IN_USE = 0x0E;
        public const uint ERR_INVALID_IOCTL_ID = 0x0F;
        public const uint ERR_BUFFER_EMPTY = 0x10;
        public const uint ERR_BUFFER_FULL = 0x11;
        public const uint ERR_BUFFER_OVERFLOW = 0x12;
        public const uint ERR_PIN_INVALID = 0x13;
        public const uint ERR_CHANNEL_IN_USE = 0x14;
        public const uint ERR_MSG_PROTOCOL_ID = 0x15;
        public const uint ERR_INVALID_FILTER_ID = 0x16;
        public const uint ERR_NO_FLOW_CONTROL = 0x17;
        public const uint ERR_NOT_UNIQUE = 0x18;
        public const uint ERR_INVALID_BAUDRATE = 0x19;
        public const uint ERR_INVALID_DEVICE_ID = 0x1A;

        private static readonly Dictionary<uint, string> _descriptions = new()
        {
            [STATUS_NOERROR] = "Success",
            [ERR_NOT_SUPPORTED] = "Feature not supported by device",
            [ERR_INVALID_CHANNEL_ID] = "Invalid channel ID",
            [ERR_INVALID_PROTOCOL_ID] = "Invalid/unsupported protocol ID",
            [ERR_NULL_PARAMETER] = "NULL pointer parameter",
            [ERR_INVALID_IOCTL_VALUE] = "Invalid IOCTL parameter value",
            [ERR_INVALID_FLAGS] = "Invalid flags parameter",
            [ERR_FAILED] = "Unspecified error occurred",
            [ERR_DEVICE_NOT_CONNECTED] = "J2534 device not connected",
            [ERR_TIMEOUT] = "Timeout - no response from ECU",
            [ERR_INVALID_MSG] = "Invalid message structure",
            [ERR_INVALID_TIME_INTERVAL] = "Invalid time interval",
            [ERR_EXCEEDED_LIMIT] = "Exceeded limit (filters/messages)",
            [ERR_INVALID_MSG_ID] = "Invalid message ID",
            [ERR_DEVICE_IN_USE] = "Device already in use",
            [ERR_INVALID_IOCTL_ID] = "Invalid IOCTL ID",
            [ERR_BUFFER_EMPTY] = "Receive buffer empty",
            [ERR_BUFFER_FULL] = "Transmit buffer full",
            [ERR_BUFFER_OVERFLOW] = "Buffer overflow occurred",
            [ERR_PIN_INVALID] = "Invalid J1962 pin configuration",
            [ERR_CHANNEL_IN_USE] = "Channel already in use",
            [ERR_MSG_PROTOCOL_ID] = "Message protocol mismatch",
            [ERR_INVALID_FILTER_ID] = "Invalid filter ID",
            [ERR_NO_FLOW_CONTROL] = "No flow control filter (required for ISO15765)",
            [ERR_NOT_UNIQUE] = "Filter already exists",
            [ERR_INVALID_BAUDRATE] = "Invalid/unsupported baud rate",
            [ERR_INVALID_DEVICE_ID] = "Invalid device ID"
        };

        public static string GetDescription(uint code) =>
            _descriptions.TryGetValue(code, out var desc) ? desc : $"Unknown error 0x{code:X4}";

        public static bool IsSuccess(uint code) => code == STATUS_NOERROR;
        
        public static bool IsRecoverable(uint code) =>
            code == ERR_TIMEOUT || code == ERR_BUFFER_EMPTY || 
            code == ERR_BUFFER_FULL || code == ERR_BUFFER_OVERFLOW;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PASSTHRU_MSG
    {
        public uint ProtocolID;
        public uint RxStatus;
        public uint TxFlags;
        public uint Timestamp;
        public uint DataSize;
        public uint ExtraDataIndex;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)]
        public byte[] Data;

        public static PASSTHRU_MSG Create(uint protocolId = 0)
        {
            return new PASSTHRU_MSG
            {
                ProtocolID = protocolId,
                Data = new byte[4128],
                ExtraDataIndex = 4128
            };
        }

        public static int Size => Marshal.SizeOf<PASSTHRU_MSG>();

        public uint GetCanId()
        {
            if (Data == null || DataSize < 4) return 0;
            return (uint)((Data[0] << 24) | (Data[1] << 16) | (Data[2] << 8) | Data[3]);
        }

        public void SetCanId(uint id)
        {
            if (Data == null) Data = new byte[4128];
            Data[0] = (byte)((id >> 24) & 0xFF);
            Data[1] = (byte)((id >> 16) & 0xFF);
            Data[2] = (byte)((id >> 8) & 0xFF);
            Data[3] = (byte)(id & 0xFF);
        }

        public ReadOnlySpan<byte> GetPayload()
        {
            if (Data == null || DataSize <= 4) return ReadOnlySpan<byte>.Empty;
            return new ReadOnlySpan<byte>(Data, 4, (int)DataSize - 4);
        }

        public byte[] GetPayloadArray()
        {
            if (Data == null || DataSize <= 4) return Array.Empty<byte>();
            var payload = new byte[DataSize - 4];
            Array.Copy(Data, 4, payload, 0, payload.Length);
            return payload;
        }

        public void SetPayload(byte[] payload)
        {
            if (Data == null) Data = new byte[4128];
            
            if (payload == null || payload.Length == 0)
            {
                DataSize = 4;
                return;
            }
            
            int len = Math.Min(payload.Length, 4124);
            Array.Copy(payload, 0, Data, 4, len);
            DataSize = (uint)(4 + len);
        }

        public void SetPayload(ReadOnlySpan<byte> payload)
        {
            if (Data == null) Data = new byte[4128];
            
            if (payload.IsEmpty)
            {
                DataSize = 4;
                return;
            }
            
            int len = Math.Min(payload.Length, 4124);
            payload.Slice(0, len).CopyTo(Data.AsSpan(4));
            DataSize = (uint)(4 + len);
        }

        public bool IsTxEcho => (RxStatus & J2534Const.TX_MSG_TYPE) != 0;
        public bool IsFirstFrame => (RxStatus & J2534Const.ISO15765_FIRST_FRAME) != 0;
        
        public string ToHexString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("ID:0x{0:X3} [{1}] ", GetCanId(), DataSize - 4);
            var payload = GetPayloadArray();
            for (int i = 0; i < Math.Min(payload.Length, 16); i++)
            {
                sb.AppendFormat("{0:X2} ", payload[i]);
            }
            if (payload.Length > 16) sb.Append("...");
            return sb.ToString().TrimEnd();
        }

        public override string ToString() => ToHexString();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SCONFIG
    {
        public uint Parameter;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SCONFIG_LIST
    {
        public uint NumOfParams;
        public IntPtr ConfigPtr;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SBYTE_ARRAY
    {
        public uint NumOfBytes;
        public IntPtr BytePtr;
    }

    #endregion

    #region J2534 Device Discovery (Enhanced)

    public class J2534DeviceInfo
    {
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string DllPath { get; set; }
        public string ConfigApplication { get; set; }
        public string RegistryPath { get; set; }
        public bool Is32Bit { get; set; }
        public List<uint> SupportedProtocols { get; set; } = new();
        public string FirmwareVersion { get; set; }
        public string DllVersion { get; set; }
        public string ApiVersion { get; set; }
        
        public bool IsAvailable => !string.IsNullOrEmpty(DllPath) && File.Exists(DllPath);
        
        public bool ArchitectureMatches => 
            (Is32Bit && !Environment.Is64BitProcess) || 
            (!Is32Bit && Environment.Is64BitProcess);

        public override string ToString()
        {
            var status = IsAvailable ? (ArchitectureMatches ? "✓" : "⚠ Arch mismatch") : "✗ Not found";
            return $"[{status}] {Name} ({Vendor}) [{(Is32Bit ? "x86" : "x64")}]";
        }
    }

    public static class J2534DeviceScanner
    {
        private const string TAG = "DeviceScan";
        
        private static readonly string[] RegistryPaths = 
        {
            @"SOFTWARE\PassThruSupport.04.04",
            @"SOFTWARE\WOW6432Node\PassThruSupport.04.04"
        };

        public static List<J2534DeviceInfo> ScanAll()
        {
            var devices = new List<J2534DeviceInfo>();
            var log = Logger.Instance;

            log.Info(TAG, "Scanning for J2534 devices...");

            foreach (var basePath in RegistryPaths)
            {
                bool is32Bit = basePath.Contains("WOW6432Node");
                ScanRegistryPath(devices, basePath, is32Bit);
            }

            // Remove duplicates by DLL path, prefer matching architecture
            devices = devices
                .GroupBy(d => d.DllPath?.ToLowerInvariant() ?? "")
                .Select(g => g.FirstOrDefault(d => d.ArchitectureMatches) ?? g.First())
                .Where(d => d != null)
                .OrderByDescending(d => d.ArchitectureMatches)
                .ThenBy(d => d.Name)
                .ToList();

            log.Info(TAG, $"Found {devices.Count} J2534 device(s):");
            foreach (var dev in devices)
            {
                log.Info(TAG, $"  {dev}");
            }

            return devices;
        }

        private static void ScanRegistryPath(List<J2534DeviceInfo> devices, string basePath, bool is32Bit)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
                if (baseKey == null) return;

                foreach (string subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceKey = baseKey.OpenSubKey(subKeyName);
                        if (deviceKey == null) continue;

                        var dllPath = deviceKey.GetValue("FunctionLibrary")?.ToString();
                        if (string.IsNullOrEmpty(dllPath)) continue;

                        var device = new J2534DeviceInfo
                        {
                            Name = deviceKey.GetValue("Name")?.ToString() ?? subKeyName,
                            Vendor = deviceKey.GetValue("Vendor")?.ToString() ?? "Unknown",
                            DllPath = dllPath,
                            ConfigApplication = deviceKey.GetValue("ConfigApplication")?.ToString(),
                            RegistryPath = $"{basePath}\\{subKeyName}",
                            Is32Bit = is32Bit
                        };

                        // Read supported protocols
                        if (uint.TryParse(deviceKey.GetValue("CAN")?.ToString(), out var can) && can > 0)
                            device.SupportedProtocols.Add(J2534Const.CAN);
                        if (uint.TryParse(deviceKey.GetValue("ISO15765")?.ToString(), out var iso) && iso > 0)
                            device.SupportedProtocols.Add(J2534Const.ISO15765);
                        if (uint.TryParse(deviceKey.GetValue("ISO14230")?.ToString(), out var kwp) && kwp > 0)
                            device.SupportedProtocols.Add(J2534Const.ISO14230);

                        devices.Add(device);
                        Logger.Instance.Debug(TAG, $"Found: {device.Name} at {dllPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Debug(TAG, $"Error reading {subKeyName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Debug(TAG, $"Cannot access {basePath}: {ex.Message}");
            }
        }

        public static List<J2534DeviceInfo> GetCompatibleDevices()
        {
            return ScanAll()
                .Where(d => d.IsAvailable && d.ArchitectureMatches)
                .Where(d => d.SupportedProtocols.Contains(J2534Const.ISO15765) ||
                           d.SupportedProtocols.Contains(J2534Const.CAN))
                .ToList();
        }

        public static J2534DeviceInfo FindPreferred()
        {
            var devices = GetCompatibleDevices();
            if (!devices.Any()) return null;

            // Preference order for professional diagnostics
            string[] preferred = 
            { 
                "Bosch", "KTS", "Tactrix", "OpenPort", "Drew", "Mongoose", 
                "VCM", "VXDIAG", "Autel", "Launch", "VCDS"
            };

            foreach (var pref in preferred)
            {
                var match = devices.FirstOrDefault(d =>
                    d.Name.Contains(pref, StringComparison.OrdinalIgnoreCase) ||
                    d.Vendor.Contains(pref, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    Logger.Instance.Info(TAG, $"Selected preferred device: {match.Name}");
                    return match;
                }
            }

            Logger.Instance.Info(TAG, $"Using first available: {devices[0].Name}");
            return devices[0];
        }
    }

    #endregion

    #region J2534 API Implementation (Complete)

    /// <summary>
    /// Complete J2534 v04.04 API wrapper with proper memory management,
    /// retry logic, architecture validation, and comprehensive error handling
    /// </summary>
    public sealed class J2534Api : IDisposable
    {
        private const string TAG = "J2534";

        #region Native Imports

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();

        #endregion

        #region Delegate Definitions

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruOpenDelegate(IntPtr pName, ref uint pDeviceID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruCloseDelegate(uint DeviceID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruConnectDelegate(
            uint DeviceID, uint ProtocolID, uint Flags, uint BaudRate, ref uint pChannelID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruDisconnectDelegate(uint ChannelID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruReadMsgsDelegate(
            uint ChannelID, IntPtr pMsg, ref uint pNumMsgs, uint Timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruWriteMsgsDelegate(
            uint ChannelID, IntPtr pMsg, ref uint pNumMsgs, uint Timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruStartMsgFilterDelegate(
            uint ChannelID, uint FilterType, IntPtr pMaskMsg, IntPtr pPatternMsg, 
            IntPtr pFlowControlMsg, ref uint pFilterID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruStopMsgFilterDelegate(uint ChannelID, uint FilterID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruStartPeriodicMsgDelegate(
            uint ChannelID, IntPtr pMsg, ref uint pMsgID, uint TimeInterval);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruStopPeriodicMsgDelegate(uint ChannelID, uint MsgID);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruIoctlDelegate(
            uint HandleID, uint IoctlID, IntPtr pInput, IntPtr pOutput);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruReadVersionDelegate(
            uint DeviceID, IntPtr pFirmwareVersion, IntPtr pDllVersion, IntPtr pApiVersion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruGetLastErrorDelegate(IntPtr pErrorDescription);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PassThruSetProgrammingVoltageDelegate(
            uint DeviceID, uint PinNumber, uint Voltage);

        #endregion

        #region Fields

        private readonly IntPtr _hDll;
        private readonly string _dllPath;
        private readonly object _apiLock = new();
        private readonly SemaphoreSlim _asyncLock = new(1, 1);
        private bool _disposed;

        // Function pointers
        private readonly PassThruOpenDelegate _ptOpen;
        private readonly PassThruCloseDelegate _ptClose;
        private readonly PassThruConnectDelegate _ptConnect;
        private readonly PassThruDisconnectDelegate _ptDisconnect;
        private readonly PassThruReadMsgsDelegate _ptReadMsgs;
        private readonly PassThruWriteMsgsDelegate _ptWriteMsgs;
        private readonly PassThruStartMsgFilterDelegate _ptStartMsgFilter;
        private readonly PassThruStopMsgFilterDelegate _ptStopMsgFilter;
        private readonly PassThruStartPeriodicMsgDelegate _ptStartPeriodicMsg;
        private readonly PassThruStopPeriodicMsgDelegate _ptStopPeriodicMsg;
        private readonly PassThruIoctlDelegate _ptIoctl;
        private readonly PassThruReadVersionDelegate _ptReadVersion;
        private readonly PassThruGetLastErrorDelegate _ptGetLastError;
        private readonly PassThruSetProgrammingVoltageDelegate _ptSetProgVoltage;

        // Diagnostics
        private readonly Stopwatch _perfTimer = new();
        private long _totalApiCalls;
        private long _totalApiTimeMs;

        #endregion

        #region Properties

        public string DllPath => _dllPath;
        public bool IsLoaded => _hDll != IntPtr.Zero;
        public double AverageApiTimeMs => _totalApiCalls > 0 ? (double)_totalApiTimeMs / _totalApiCalls : 0;

        #endregion

        #region Constructor

        public J2534Api(string dllPath)
        {
            _dllPath = dllPath ?? throw new ArgumentNullException(nameof(dllPath));

            if (!File.Exists(dllPath))
                throw new FileNotFoundException($"J2534 DLL not found: {dllPath}");

            ValidateArchitecture();

            _hDll = LoadLibraryW(dllPath);
            if (_hDll == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"Failed to load J2534 DLL.\n" +
                    $"Path: {dllPath}\n" +
                    $"Win32 Error: {error} (0x{error:X8})\n" +
                    $"Ensure Visual C++ Redistributable (x86/x64) is installed.");
            }

            Logger.Instance.Info(TAG, $"Loaded: {Path.GetFileName(dllPath)}");

            // Load all function pointers
            _ptOpen = LoadFunction<PassThruOpenDelegate>("PassThruOpen");
            _ptClose = LoadFunction<PassThruCloseDelegate>("PassThruClose");
            _ptConnect = LoadFunction<PassThruConnectDelegate>("PassThruConnect");
            _ptDisconnect = LoadFunction<PassThruDisconnectDelegate>("PassThruDisconnect");
            _ptReadMsgs = LoadFunction<PassThruReadMsgsDelegate>("PassThruReadMsgs");
            _ptWriteMsgs = LoadFunction<PassThruWriteMsgsDelegate>("PassThruWriteMsgs");
            _ptStartMsgFilter = LoadFunction<PassThruStartMsgFilterDelegate>("PassThruStartMsgFilter");
            _ptStopMsgFilter = LoadFunction<PassThruStopMsgFilterDelegate>("PassThruStopMsgFilter");
            _ptStartPeriodicMsg = LoadFunction<PassThruStartPeriodicMsgDelegate>("PassThruStartPeriodicMsg");
            _ptStopPeriodicMsg = LoadFunction<PassThruStopPeriodicMsgDelegate>("PassThruStopPeriodicMsg");
            _ptIoctl = LoadFunction<PassThruIoctlDelegate>("PassThruIoctl");
            _ptReadVersion = LoadFunction<PassThruReadVersionDelegate>("PassThruReadVersion");
            _ptGetLastError = LoadFunction<PassThruGetLastErrorDelegate>("PassThruGetLastError");
            
            // Optional J2534-2 function
            _ptSetProgVoltage = LoadFunctionOptional<PassThruSetProgrammingVoltageDelegate>("PassThruSetProgrammingVoltage");

            Logger.Instance.Debug(TAG, "All J2534 functions loaded successfully");
        }

        private void ValidateArchitecture()
        {
            try
            {
                using var fs = new FileStream(_dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);

                // DOS header check
                if (br.ReadUInt16() != 0x5A4D)
                    throw new BadImageFormatException("Invalid PE file - not a valid DLL");

                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();

                fs.Seek(peOffset, SeekOrigin.Begin);
                if (br.ReadUInt32() != 0x00004550)
                    throw new BadImageFormatException("Invalid PE signature");

                ushort machine = br.ReadUInt16();
                bool is64BitDll = machine == 0x8664;
                bool is64BitProcess = Environment.Is64BitProcess;

                if (is64BitDll != is64BitProcess)
                {
                    string required = is64BitProcess ? "64-bit (x64)" : "32-bit (x86)";
                    string dllArch = is64BitDll ? "64-bit" : "32-bit";
                    throw new BadImageFormatException(
                        $"Architecture mismatch!\n" +
                        $"Application: {required}\n" +
                        $"DLL: {dllArch}\n" +
                        $"Solution: Use matching DLL or recompile application as {(is64BitDll ? "x64" : "x86")}");
                }

                Logger.Instance.Debug(TAG, $"Architecture verified: {(is64BitDll ? "x64" : "x86")}");
            }
            catch (BadImageFormatException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Instance.Warn(TAG, $"Architecture check skipped: {ex.Message}");
            }
        }

        private T LoadFunction<T>(string name) where T : Delegate
        {
            IntPtr ptr = GetProcAddress(_hDll, name);
            if (ptr == IntPtr.Zero)
                throw new EntryPointNotFoundException($"J2534 function not found: {name}");
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        private T LoadFunctionOptional<T>(string name) where T : Delegate
        {
            IntPtr ptr = GetProcAddress(_hDll, name);
            if (ptr == IntPtr.Zero)
            {
                Logger.Instance.Debug(TAG, $"Optional function not available: {name}");
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        #endregion

        #region Core API Methods

        public uint PassThruOpen(out uint deviceId)
        {
            uint localDeviceId = 0;

            uint result = ExecuteWithTiming(() =>
            {
                uint id = 0;
                uint r = _ptOpen(IntPtr.Zero, ref id);
                localDeviceId = id;   // capture in local variable
                return r;
            }, "PassThruOpen");

            deviceId = localDeviceId;
            return result;
        }

        public uint PassThruClose(uint deviceId)
        {
            return ExecuteWithTiming(() => _ptClose(deviceId), "PassThruClose");
        }

        public uint PassThruConnect(uint deviceId, uint protocolId, uint flags,
            uint baudRate, out uint channelId)
        {
            uint localChannelId = 0;

            uint result = ExecuteWithTiming(() =>
            {
                uint ch = 0;
                uint r = _ptConnect(deviceId, protocolId, flags, baudRate, ref ch);
                localChannelId = ch;  // capture in local variable
                return r;
            }, "PassThruConnect");

            channelId = localChannelId;
            return result;
        }

        public uint PassThruDisconnect(uint channelId)
        {
            return ExecuteWithTiming(() => _ptDisconnect(channelId), "PassThruDisconnect");
        }

        #endregion

        #region Message I/O

        public uint PassThruReadMsgs(uint channelId, PASSTHRU_MSG[] msgs, 
            ref uint numMsgs, uint timeout)
        {
            if (msgs == null || msgs.Length == 0)
            {
                numMsgs = 0;
                return J2534Error.ERR_NULL_PARAMETER;
            }

            // Initialize messages if needed
            for (int i = 0; i < msgs.Length; i++)
            {
                if (msgs[i].Data == null)
                    msgs[i] = PASSTHRU_MSG.Create();
            }

            int structSize = PASSTHRU_MSG.Size;
            IntPtr pMsgs = Marshal.AllocHGlobal(structSize * msgs.Length);

            try
            {
                // Marshal to unmanaged memory
                for (int i = 0; i < msgs.Length; i++)
                {
                    IntPtr pCurrent = IntPtr.Add(pMsgs, i * structSize);
                    Marshal.StructureToPtr(msgs[i], pCurrent, false);
                }

                uint result;
                lock (_apiLock)
                {
                    result = _ptReadMsgs(channelId, pMsgs, ref numMsgs, timeout);
                }

                // Marshal back received messages
                for (int i = 0; i < Math.Min(numMsgs, (uint)msgs.Length); i++)
                {
                    IntPtr pCurrent = IntPtr.Add(pMsgs, i * structSize);
                    msgs[i] = Marshal.PtrToStructure<PASSTHRU_MSG>(pCurrent);
                }

                return result;
            }
            finally
            {
                // Clean up
                for (int i = 0; i < msgs.Length; i++)
                {
                    try
                    {
                        IntPtr pCurrent = IntPtr.Add(pMsgs, i * structSize);
                        Marshal.DestroyStructure<PASSTHRU_MSG>(pCurrent);
                    }
                    catch { }
                }
                Marshal.FreeHGlobal(pMsgs);
            }
        }

        public uint PassThruWriteMsgs(uint channelId, PASSTHRU_MSG[] msgs, 
            ref uint numMsgs, uint timeout)
        {
            if (msgs == null || msgs.Length == 0)
            {
                numMsgs = 0;
                return J2534Error.ERR_NULL_PARAMETER;
            }

            int structSize = PASSTHRU_MSG.Size;
            IntPtr pMsgs = Marshal.AllocHGlobal(structSize * msgs.Length);

            try
            {
                for (int i = 0; i < msgs.Length; i++)
                {
                    if (msgs[i].Data == null)
                        msgs[i].Data = new byte[4128];
                    
                    IntPtr pCurrent = IntPtr.Add(pMsgs, i * structSize);
                    Marshal.StructureToPtr(msgs[i], pCurrent, false);
                }

                lock (_apiLock)
                {
                    return _ptWriteMsgs(channelId, pMsgs, ref numMsgs, timeout);
                }
            }
            finally
            {
                for (int i = 0; i < msgs.Length; i++)
                {
                    try
                    {
                        IntPtr pCurrent = IntPtr.Add(pMsgs, i * structSize);
                        Marshal.DestroyStructure<PASSTHRU_MSG>(pCurrent);
                    }
                    catch { }
                }
                Marshal.FreeHGlobal(pMsgs);
            }
        }

        /// <summary>
        /// Simplified single message write
        /// </summary>
        public uint WriteSingleMsg(uint channelId, PASSTHRU_MSG msg, uint timeout = 1000)
        {
            var msgs = new[] { msg };
            uint count = 1;
            return PassThruWriteMsgs(channelId, msgs, ref count, timeout);
        }

        /// <summary>
        /// Read available messages with proper timeout handling
        /// </summary>
        public (uint result, List<PASSTHRU_MSG> messages) ReadMessages(uint channelId, 
            uint maxMsgs = 10, uint timeout = 100)
        {
            var msgs = new PASSTHRU_MSG[maxMsgs];
            for (int i = 0; i < maxMsgs; i++)
                msgs[i] = PASSTHRU_MSG.Create();

            uint numMsgs = maxMsgs;
            uint result = PassThruReadMsgs(channelId, msgs, ref numMsgs, timeout);

            var list = new List<PASSTHRU_MSG>();
            if (result == J2534Error.STATUS_NOERROR || result == J2534Error.ERR_BUFFER_EMPTY)
            {
                for (int i = 0; i < numMsgs; i++)
                {
                    if (!msgs[i].IsTxEcho && msgs[i].DataSize > 4)
                        list.Add(msgs[i]);
                }
            }

            return (result, list);
        }

        #endregion

        #region Filter Management

        public uint PassThruStartMsgFilter(uint channelId, uint filterType,
            PASSTHRU_MSG maskMsg, PASSTHRU_MSG patternMsg, PASSTHRU_MSG? flowControlMsg,
            out uint filterId)
        {
            filterId = 0;
            
            IntPtr pMask = IntPtr.Zero;
            IntPtr pPattern = IntPtr.Zero;
            IntPtr pFlow = IntPtr.Zero;
            int structSize = PASSTHRU_MSG.Size;

            try
            {
                pMask = Marshal.AllocHGlobal(structSize);
                pPattern = Marshal.AllocHGlobal(structSize);

                Marshal.StructureToPtr(maskMsg, pMask, false);
                Marshal.StructureToPtr(patternMsg, pPattern, false);

                if (flowControlMsg.HasValue)
                {
                    pFlow = Marshal.AllocHGlobal(structSize);
                    Marshal.StructureToPtr(flowControlMsg.Value, pFlow, false);
                }

                uint fid = 0;
                uint result;
                lock (_apiLock)
                {
                    result = _ptStartMsgFilter(channelId, filterType, pMask, pPattern, pFlow, ref fid);
                }
                filterId = fid;

                if (J2534Error.IsSuccess(result))
                {
                    Logger.Instance.Debug(TAG, 
                        $"Filter {filterId} created: Type={filterType}, Pattern=0x{patternMsg.GetCanId():X3}");
                }

                return result;
            }
            finally
            {
                SafeFree<PASSTHRU_MSG>(pMask);
                SafeFree<PASSTHRU_MSG>(pPattern);
                SafeFree<PASSTHRU_MSG>(pFlow);
            }
        }

        public uint PassThruStopMsgFilter(uint channelId, uint filterId)
        {
            lock (_apiLock)
            {
                return _ptStopMsgFilter(channelId, filterId);
            }
        }

        /// <summary>
        /// Set up ISO 15765 flow control filter (required for CAN diagnostics)
        /// </summary>
        public uint SetupFlowControlFilter(uint channelId, uint txId, uint rxId, out uint filterId)
        {
            filterId = 0;

            // Create mask - match all bits for 11-bit IDs
            var mask = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            mask.TxFlags = VehicleConfig.USE_29BIT_IDS ? J2534Const.CAN_29BIT_TX : 0;
            mask.SetCanId(VehicleConfig.FILTER_MASK_11BIT);
            mask.DataSize = 4;

            // Pattern - the response ID we want to receive
            var pattern = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            pattern.TxFlags = mask.TxFlags;
            pattern.SetCanId(rxId);
            pattern.DataSize = 4;

            // Flow control - the ID we transmit on
            var flowControl = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            flowControl.TxFlags = mask.TxFlags;
            flowControl.SetCanId(txId);
            flowControl.DataSize = 4;

            return PassThruStartMsgFilter(channelId, J2534Const.FLOW_CONTROL_FILTER,
                mask, pattern, flowControl, out filterId);
        }

        /// <summary>
        /// Set up pass filter for broadcast responses
        /// </summary>
        public uint SetupPassFilter(uint channelId, uint minId, uint maxId, out uint filterId)
        {
            filterId = 0;

            // Calculate mask that covers the ID range
            uint maskValue = ~(minId ^ maxId) & VehicleConfig.FILTER_MASK_11BIT;

            var mask = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            mask.SetCanId(maskValue);
            mask.DataSize = 4;

            var pattern = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            pattern.SetCanId(minId & maskValue);
            pattern.DataSize = 4;

            return PassThruStartMsgFilter(channelId, J2534Const.PASS_FILTER,
                mask, pattern, null, out filterId);
        }

        #endregion

        #region Periodic Messages

        public uint PassThruStartPeriodicMsg(uint channelId, PASSTHRU_MSG msg, 
            out uint msgId, uint intervalMs)
        {
            msgId = 0;
            IntPtr pMsg = Marshal.AllocHGlobal(PASSTHRU_MSG.Size);

            try
            {
                Marshal.StructureToPtr(msg, pMsg, false);
                
                uint id = 0;
                uint result;
                lock (_apiLock)
                {
                    result = _ptStartPeriodicMsg(channelId, pMsg, ref id, intervalMs);
                }
                msgId = id;
                return result;
            }
            finally
            {
                SafeFree<PASSTHRU_MSG>(pMsg);
            }
        }

        public uint PassThruStopPeriodicMsg(uint channelId, uint msgId)
        {
            lock (_apiLock)
            {
                return _ptStopPeriodicMsg(channelId, msgId);
            }
        }

        #endregion

        #region IOCTL Operations

        public uint PassThruIoctl(uint handleId, uint ioctlId, 
            IntPtr pInput = default, IntPtr pOutput = default)
        {
            lock (_apiLock)
            {
                return _ptIoctl(handleId, ioctlId, pInput, pOutput);
            }
        }

        public uint ClearBuffers(uint channelId)
        {
            uint result1 = PassThruIoctl(channelId, J2534Const.CLEAR_RX_BUFFER);
            uint result2 = PassThruIoctl(channelId, J2534Const.CLEAR_TX_BUFFER);
            return result1 != J2534Error.STATUS_NOERROR ? result1 : result2;
        }

        public uint ClearFilters(uint channelId)
        {
            return PassThruIoctl(channelId, J2534Const.CLEAR_MSG_FILTERS);
        }

        public uint ReadBatteryVoltage(uint deviceId, out double voltage)
        {
            voltage = 0;
            IntPtr pVoltage = Marshal.AllocHGlobal(sizeof(uint));

            try
            {
                uint result = PassThruIoctl(deviceId, J2534Const.READ_VBATT, IntPtr.Zero, pVoltage);

                if (J2534Error.IsSuccess(result))
                {
                    uint millivolts = (uint)Marshal.ReadInt32(pVoltage);
                    voltage = millivolts / 1000.0;
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pVoltage);
            }
        }

        public uint SetConfiguration(uint channelId, params (uint param, uint value)[] configs)
        {
            if (configs == null || configs.Length == 0)
                return J2534Error.STATUS_NOERROR;

            int configSize = Marshal.SizeOf<SCONFIG>();
            IntPtr pConfigs = Marshal.AllocHGlobal(configSize * configs.Length);
            IntPtr pConfigList = Marshal.AllocHGlobal(Marshal.SizeOf<SCONFIG_LIST>());

            try
            {
                for (int i = 0; i < configs.Length; i++)
                {
                    var sconfig = new SCONFIG
                    {
                        Parameter = configs[i].param,
                        Value = configs[i].value
                    };
                    IntPtr pCurrent = IntPtr.Add(pConfigs, i * configSize);
                    Marshal.StructureToPtr(sconfig, pCurrent, false);
                }

                var configList = new SCONFIG_LIST
                {
                    NumOfParams = (uint)configs.Length,
                    ConfigPtr = pConfigs
                };
                Marshal.StructureToPtr(configList, pConfigList, false);

                return PassThruIoctl(channelId, J2534Const.SET_CONFIG, pConfigList);
            }
            finally
            {
                for (int i = 0; i < configs.Length; i++)
                {
                    try
                    {
                        IntPtr pCurrent = IntPtr.Add(pConfigs, i * configSize);
                        Marshal.DestroyStructure<SCONFIG>(pCurrent);
                    }
                    catch { }
                }
                Marshal.FreeHGlobal(pConfigs);
                
                try { Marshal.DestroyStructure<SCONFIG_LIST>(pConfigList); } catch { }
                Marshal.FreeHGlobal(pConfigList);
            }
        }

        public uint GetConfiguration(uint channelId, uint parameter, out uint value)
        {
            value = 0;
            
            int configSize = Marshal.SizeOf<SCONFIG>();
            IntPtr pConfig = Marshal.AllocHGlobal(configSize);
            IntPtr pConfigList = Marshal.AllocHGlobal(Marshal.SizeOf<SCONFIG_LIST>());

            try
            {
                var sconfig = new SCONFIG { Parameter = parameter, Value = 0 };
                Marshal.StructureToPtr(sconfig, pConfig, false);

                var configList = new SCONFIG_LIST { NumOfParams = 1, ConfigPtr = pConfig };
                Marshal.StructureToPtr(configList, pConfigList, false);

                uint result = PassThruIoctl(channelId, J2534Const.GET_CONFIG, pConfigList);

                if (J2534Error.IsSuccess(result))
                {
                    sconfig = Marshal.PtrToStructure<SCONFIG>(pConfig);
                    value = sconfig.Value;
                }

                return result;
            }
            finally
            {
                try { Marshal.DestroyStructure<SCONFIG>(pConfig); } catch { }
                Marshal.FreeHGlobal(pConfig);
                
                try { Marshal.DestroyStructure<SCONFIG_LIST>(pConfigList); } catch { }
                Marshal.FreeHGlobal(pConfigList);
            }
        }

        #endregion

        #region Version Information

        public uint ReadVersion(uint deviceId, out string firmware, out string dll, out string api)
        {
            firmware = dll = api = "";
            
            IntPtr pFw = Marshal.AllocHGlobal(256);
            IntPtr pDll = Marshal.AllocHGlobal(256);
            IntPtr pApi = Marshal.AllocHGlobal(256);

            try
            {
                // Zero memory
                for (int i = 0; i < 256; i++)
                {
                    Marshal.WriteByte(pFw, i, 0);
                    Marshal.WriteByte(pDll, i, 0);
                    Marshal.WriteByte(pApi, i, 0);
                }

                uint result;
                lock (_apiLock)
                {
                    result = _ptReadVersion(deviceId, pFw, pDll, pApi);
                }

                if  (J2534Error.IsSuccess(result))
                {
                    firmware = Marshal.PtrToStringAnsi(pFw) ?? "";
                    dll = Marshal.PtrToStringAnsi(pDll) ?? "";
                    api = Marshal.PtrToStringAnsi(pApi) ?? "";
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pFw);
                Marshal.FreeHGlobal(pDll);
                Marshal.FreeHGlobal(pApi);
            }
        }


        #endregion

        #region Helper Methods

        private uint ExecuteWithTiming(Func<uint> action, string name)
        {
            _perfTimer.Restart();
            uint result;
            
            lock (_apiLock)
            {
                result = action();
            }
            
            _perfTimer.Stop();
            _totalApiCalls++;
            _totalApiTimeMs += _perfTimer.ElapsedMilliseconds;

            if (!J2534Error.IsSuccess(result) && result != J2534Error.ERR_BUFFER_EMPTY)
            {
                Logger.Instance.Log(LogLevel.Warning, LogCategory.J2534, TAG,
                    $"{name} failed: {J2534Error.GetDescription(result)} (0x{result:X2})");
            }

            return result;
        }

        private void SafeFree<T>(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return;
            try
            {
                Marshal.DestroyStructure<T>(ptr);
            }
            catch { }
            Marshal.FreeHGlobal(ptr);
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_hDll != IntPtr.Zero)
            {
                FreeLibrary(_hDll);
                Logger.Instance.Debug(TAG, "J2534 DLL unloaded");
            }

            _asyncLock?.Dispose();
        }

        #endregion
    }

    #endregion

    #region ISO-TP Transport Layer (ISO 15765-2)

    /// <summary>
    /// ISO 15765-2 (ISO-TP) segmented message handler for CAN diagnostics
    /// Handles Single Frame, First Frame, Consecutive Frame, Flow Control
    /// </summary>
    public sealed class IsoTpHandler
    {
        private const string TAG = "ISO-TP";
        
        // Frame type nibbles
        private const byte FRAME_SINGLE = 0x00;
        private const byte FRAME_FIRST = 0x10;
        private const byte FRAME_CONSECUTIVE = 0x20;
        private const byte FRAME_FLOW_CONTROL = 0x30;
        
        // Flow status
        private const byte FLOW_CONTINUE = 0x00;
        private const byte FLOW_WAIT = 0x01;
        private const byte FLOW_OVERFLOW = 0x02;

        private readonly J2534Api _api;
        private readonly uint _channelId;
        private readonly uint _txId;
        private readonly uint _rxId;
        private readonly object _lock = new();
        
        private byte _blockSize = VehicleConfig.ISO_TP_BLOCK_SIZE;
        private byte _stMin = VehicleConfig.ISO_TP_ST_MIN;
        private int _rxTimeoutMs = VehicleConfig.RX_TIMEOUT_MS;
        private int _txTimeoutMs = VehicleConfig.TX_TIMEOUT_MS;

        public IsoTpHandler(J2534Api api, uint channelId, uint txId, uint rxId)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _channelId = channelId;
            _txId = txId;
            _rxId = rxId;
        }

        public void SetTimeouts(int rxMs, int txMs)
        {
            _rxTimeoutMs = rxMs;
            _txTimeoutMs = txMs;
        }

        /// <summary>
        /// Send data using ISO-TP segmentation if needed
        /// </summary>
        public bool Send(byte[] data, CancellationToken ct = default)
        {
            if (data == null || data.Length == 0)
                return false;

            lock (_lock)
            {
                // Clear any pending messages
                _api.ClearBuffers(_channelId);

                if (data.Length <= 7)
                {
                    // Single frame - data fits in one CAN frame
                    return SendSingleFrame(data);
                }
                else
                {
                    // Multi-frame - need segmentation
                    return SendMultiFrame(data, ct);
                }
            }
        }

        private bool SendSingleFrame(byte[] data)
        {
            var frame = new byte[8];
            frame[0] = (byte)(FRAME_SINGLE | (data.Length & 0x0F));
            Array.Copy(data, 0, frame, 1, data.Length);
            
            // Pad with 0xAA (ISO 15765-2)
            for (int i = 1 + data.Length; i < 8; i++)
                frame[i] = 0xAA;

            var msg = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            msg.TxFlags = J2534Const.ISO15765_FRAME_PAD;
            msg.SetCanId(_txId);
            msg.SetPayload(frame);

            uint result = _api.WriteSingleMsg(_channelId, msg, (uint)_txTimeoutMs);
            
            if (J2534Error.IsSuccess(result))
            {
                Logger.Instance.Protocol(TAG, $"TX SF [{data.Length}]: {BitConverter.ToString(data)}");
                return true;
            }
            
            Logger.Instance.Error(TAG, $"SF send failed: {J2534Error.GetDescription(result)}");
            return false;
        }

        private bool SendMultiFrame(byte[] data, CancellationToken ct)
        {
            int totalLen = data.Length;
            
            // First Frame: [10 LL] [LL] [6 data bytes]
            var ff = new byte[8];
            ff[0] = (byte)(FRAME_FIRST | ((totalLen >> 8) & 0x0F));
            ff[1] = (byte)(totalLen & 0xFF);
            Array.Copy(data, 0, ff, 2, 6);

            var msg = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            msg.TxFlags = J2534Const.ISO15765_FRAME_PAD;
            msg.SetCanId(_txId);
            msg.SetPayload(ff);

            uint result = _api.WriteSingleMsg(_channelId, msg, (uint)_txTimeoutMs);
            if (!J2534Error.IsSuccess(result))
            {
                Logger.Instance.Error(TAG, $"FF send failed: {J2534Error.GetDescription(result)}");
                return false;
            }

            Logger.Instance.Protocol(TAG, $"TX FF [{totalLen}]: {BitConverter.ToString(ff)}");

            // Wait for Flow Control
            var fc = WaitForFlowControl(ct);
            if (fc == null)
            {
                Logger.Instance.Error(TAG, "No flow control received");
                return false;
            }

            byte flowStatus = (byte)(fc[0] & 0x0F);
            byte blockSize = fc.Length > 1 ? fc[1] : (byte)0;
            byte stMin = fc.Length > 2 ? fc[2] : (byte)0;

            if (flowStatus != FLOW_CONTINUE)
            {
                Logger.Instance.Error(TAG, $"Flow control rejected: status={flowStatus}");
                return false;
            }

            Logger.Instance.Protocol(TAG, $"RX FC: BS={blockSize}, STmin={stMin}ms");

            // Send Consecutive Frames
            int offset = 6;
            int seqNum = 1;
            int blockCount = 0;

            while (offset < totalLen)
            {
                if (ct.IsCancellationRequested)
                    return false;

                int remaining = totalLen - offset;
                int cfLen = Math.Min(7, remaining);

                var cf = new byte[8];
                cf[0] = (byte)(FRAME_CONSECUTIVE | (seqNum & 0x0F));
                Array.Copy(data, offset, cf, 1, cfLen);
                
                // Pad remainder
                for (int i = 1 + cfLen; i < 8; i++)
                    cf[i] = 0xAA;

                msg = PASSTHRU_MSG.Create(J2534Const.ISO15765);
                msg.TxFlags = J2534Const.ISO15765_FRAME_PAD;
                msg.SetCanId(_txId);
                msg.SetPayload(cf);

                result = _api.WriteSingleMsg(_channelId, msg, (uint)_txTimeoutMs);
                if (!J2534Error.IsSuccess(result))
                {
                    Logger.Instance.Error(TAG, $"CF{seqNum} send failed");
                    return false;
                }

                Logger.Instance.Protocol(TAG, $"TX CF{seqNum}: {BitConverter.ToString(cf)}");

                offset += cfLen;
                seqNum = (seqNum + 1) & 0x0F;
                blockCount++;

                // Separation time
                if (stMin > 0 && stMin < 0x80)
                    Thread.Sleep(stMin);
                else if (stMin >= 0xF1 && stMin <= 0xF9)
                    Thread.Sleep(1); // 100-900 microseconds, round up

                // Block size check
                if (blockSize > 0 && blockCount >= blockSize && offset < totalLen)
                {
                    fc = WaitForFlowControl(ct);
                    if (fc == null || (fc[0] & 0x0F) != FLOW_CONTINUE)
                    {
                        Logger.Instance.Error(TAG, "Flow control not received after block");
                        return false;
                    }
                    blockCount = 0;
                }
            }

            return true;
        }

        private byte[] WaitForFlowControl(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            
            while (sw.ElapsedMilliseconds < _rxTimeoutMs && !ct.IsCancellationRequested)
            {
                var (result, messages) = _api.ReadMessages(_channelId, 5, 50);
                
                foreach (var msg in messages)
                {
                    if (msg.GetCanId() == _rxId && msg.DataSize > 4)
                    {
                        var payload = msg.GetPayloadArray();
                        if (payload.Length > 0 && (payload[0] & 0xF0) == FRAME_FLOW_CONTROL)
                        {
                            return payload;
                        }
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Receive complete ISO-TP message (with reassembly)
        /// </summary>
        public byte[] Receive(int timeoutMs = 0, CancellationToken ct = default)
        {
            if (timeoutMs <= 0)
                timeoutMs = _rxTimeoutMs;

            lock (_lock)
            {
                var sw = Stopwatch.StartNew();
                byte[] buffer = null;
                int expectedLen = 0;
                int receivedLen = 0;
                int nextSeq = 1;

                while (sw.ElapsedMilliseconds < timeoutMs && !ct.IsCancellationRequested)
                {
                    var (result, messages) = _api.ReadMessages(_channelId, 10, 50);

                    foreach (var msg in messages)
                    {
                        if (msg.GetCanId() != _rxId)
                            continue;

                        var payload = msg.GetPayloadArray();
                        if (payload.Length == 0)
                            continue;

                        byte pci = payload[0];
                        byte frameType = (byte)(pci & 0xF0);

                        switch (frameType)
                        {
                            case FRAME_SINGLE:
                                int sfLen = pci & 0x0F;
                                if (sfLen > 0 && sfLen <= payload.Length - 1)
                                {
                                    var data = new byte[sfLen];
                                    Array.Copy(payload, 1, data, 0, sfLen);
                                    Logger.Instance.Protocol(TAG, 
                                        $"RX SF [{sfLen}]: {BitConverter.ToString(data)}");
                                    return data;
                                }
                                break;

                            case FRAME_FIRST:
                                expectedLen = ((pci & 0x0F) << 8) | payload[1];
                                buffer = new byte[expectedLen];
                                receivedLen = Math.Min(6, expectedLen);
                                Array.Copy(payload, 2, buffer, 0, receivedLen);
                                nextSeq = 1;

                                Logger.Instance.Protocol(TAG, 
                                    $"RX FF: Total={expectedLen}, Got={receivedLen}");

                                // Send Flow Control
                                SendFlowControl(FLOW_CONTINUE, _blockSize, _stMin);
                                break;

                            case FRAME_CONSECUTIVE:
                                if (buffer == null)
                                {
                                    Logger.Instance.Warn(TAG, "CF received without FF");
                                    continue;
                                }

                                int seq = pci & 0x0F;
                                if (seq != nextSeq)
                                {
                                    Logger.Instance.Error(TAG, 
                                        $"Sequence error: expected {nextSeq}, got {seq}");
                                    return null;
                                }

                                int cfLen = Math.Min(7, expectedLen - receivedLen);
                                Array.Copy(payload, 1, buffer, receivedLen, cfLen);
                                receivedLen += cfLen;
                                nextSeq = (nextSeq + 1) & 0x0F;

                                Logger.Instance.Protocol(TAG, 
                                    $"RX CF{seq}: {receivedLen}/{expectedLen}");

                                if (receivedLen >= expectedLen)
                                {
                                    Logger.Instance.Protocol(TAG, 
                                        $"Complete: {BitConverter.ToString(buffer)}");
                                    return buffer;
                                }
                                break;

                            case FRAME_FLOW_CONTROL:
                                // Unexpected FC - log it
                                Logger.Instance.Debug(TAG, "Unexpected FC received");
                                break;
                        }
                    }
                }

                if (buffer != null && receivedLen > 0)
                {
                    Logger.Instance.Warn(TAG, 
                        $"Incomplete message: {receivedLen}/{expectedLen}");
                }

                return null;
            }
        }

        private void SendFlowControl(byte status, byte bs, byte stMin)
        {
            var fc = new byte[8];
            fc[0] = (byte)(FRAME_FLOW_CONTROL | (status & 0x0F));
            fc[1] = bs;
            fc[2] = stMin;
            // Pad
            for (int i = 3; i < 8; i++)
                fc[i] = 0xAA;

            var msg = PASSTHRU_MSG.Create(J2534Const.ISO15765);
            msg.TxFlags = J2534Const.ISO15765_FRAME_PAD;
            msg.SetCanId(_txId);
            msg.SetPayload(fc);

            _api.WriteSingleMsg(_channelId, msg, (uint)_txTimeoutMs);
            Logger.Instance.Protocol(TAG, $"TX FC: Status={status}, BS={bs}, STmin={stMin}");
        }

        /// <summary>
        /// Send request and wait for response (transaction)
        /// </summary>
        public byte[] SendAndReceive(byte[] request, int timeoutMs = 0, 
            CancellationToken ct = default)
        {
            if (!Send(request, ct))
                return null;

            return Receive(timeoutMs, ct);
        }
    }

    #endregion

    #region UDS Protocol Handler

    /// <summary>
    /// ISO 14229 UDS Protocol implementation with Hyundai extensions
    /// </summary>
    public sealed class UdsClient : IDisposable
    {
        private const string TAG = "UDS";

        private readonly J2534Api _api;
        private readonly IsoTpHandler _transport;
        private readonly uint _channelId;
        private readonly EcuDefinition _ecu;
        
        private Timer _testerPresentTimer;
        private bool _sessionActive;
        private byte _currentSession = UDS.DSC_DEFAULT_SESSION;
        private bool _securityUnlocked;
        private readonly object _lock = new();
        private bool _disposed;

        // Response handling
        private const int MAX_PENDING_RESPONSES = 50;
        private const int PENDING_WAIT_MS = 100;

        public EcuDefinition Ecu => _ecu;
        public byte CurrentSession => _currentSession;
        public bool IsSecurityUnlocked => _securityUnlocked;

        public UdsClient(J2534Api api, uint channelId, EcuDefinition ecu)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _channelId = channelId;
            _ecu = ecu ?? throw new ArgumentNullException(nameof(ecu));
            
            _transport = new IsoTpHandler(api, channelId, ecu.RequestId, ecu.ResponseId);
        }

        /// <summary>
        /// Send UDS request and receive response with NRC handling
        /// </summary>
        public UdsResponse SendRequest(byte[] request, int timeoutMs = 0, 
            CancellationToken ct = default)
        {
            if (request == null || request.Length == 0)
                return UdsResponse.Error("Empty request");

            if (timeoutMs <= 0)
                timeoutMs = VehicleConfig.RX_TIMEOUT_MS;

            lock (_lock)
            {
                int pendingCount = 0;
                
                while (pendingCount < MAX_PENDING_RESPONSES && !ct.IsCancellationRequested)
                {
                    var response = _transport.SendAndReceive(request, timeoutMs, ct);

                    if (response == null)
                        return UdsResponse.Timeout();

                    if (response.Length == 0)
                        return UdsResponse.Error("Empty response");

                    // Check for negative response
                    if (response[0] == UDS.NEGATIVE_RESPONSE)
                    {
                        if (response.Length >= 3)
                        {
                            byte sid = response[1];
                            byte nrc = response[2];

                            // Response pending - wait and retry
                            if (nrc == NRC.RESPONSE_PENDING)
                            {
                                pendingCount++;
                                Logger.Instance.Debug(TAG, 
                                    $"Response pending ({pendingCount}/{MAX_PENDING_RESPONSES})");
                                Thread.Sleep(PENDING_WAIT_MS);
                                
                                // Don't resend, just wait for response
                                response = _transport.Receive(
                                    VehicleConfig.P2_STAR_CLIENT_MAX_MS, ct);
                                
                                if (response != null && response.Length > 0 && 
                                    response[0] != UDS.NEGATIVE_RESPONSE)
                                {
                                    return UdsResponse.Positive(response);
                                }
                                continue;
                            }

                            return UdsResponse.Negative(sid, nrc);
                        }
                        return UdsResponse.Error("Malformed negative response");
                    }

                    // Positive response
                    byte expectedSid = (byte)(request[0] + UDS.POSITIVE_RESPONSE_OFFSET);
                    if (response[0] == expectedSid)
                    {
                        return UdsResponse.Positive(response);
                    }

                    // Unexpected response
                    return UdsResponse.Error(
                        $"Unexpected SID: 0x{response[0]:X2} (expected 0x{expectedSid:X2})");
                }

                return UdsResponse.Error("Max pending responses exceeded");
            }
        }

        #region Diagnostic Session Control

        public UdsResponse StartSession(byte sessionType)
        {
            Logger.Instance.Diag(TAG, $"Starting session 0x{sessionType:X2} on {_ecu.Name}");
            
            var response = SendRequest(new[] { UDS.DIAGNOSTIC_SESSION_CONTROL, sessionType });
            
            if (response.IsPositive)
            {
                _currentSession = sessionType;
                _sessionActive = true;
                
                // Start tester present for extended sessions
                if (sessionType != UDS.DSC_DEFAULT_SESSION)
                {
                    StartTesterPresent();
                }
                
                Logger.Instance.Diag(TAG, 
                    $"Session 0x{sessionType:X2} active on {_ecu.Name}");
            }
            else
            {
                Logger.Instance.Warn(TAG, 
                    $"Session change failed: {response.ErrorMessage}");
            }

            return response;
        }

        public UdsResponse StartExtendedSession() => StartSession(UDS.DSC_EXTENDED_SESSION);
        public UdsResponse StartProgrammingSession() => StartSession(UDS.DSC_PROGRAMMING_SESSION);
        public UdsResponse StartDefaultSession() 
        {
            StopTesterPresent();
            _securityUnlocked = false;
            return StartSession(UDS.DSC_DEFAULT_SESSION);
        }

        private void StartTesterPresent()
        {
            StopTesterPresent();
            
            _testerPresentTimer = new Timer(_ =>
            {
                if (!_sessionActive || _disposed) return;
                
                try
                {
                    // Suppress positive response
                    var tp = new byte[] { 
                        UDS.TESTER_PRESENT, 
                        (byte)(UDS.TP_ZERO_SUBFUNCTION | UDS.TP_SUPPRESS_RESPONSE) 
                    };
                    _transport.Send(tp);
                    Logger.Instance.Trace(TAG, "TesterPresent sent");
                }
                catch (Exception ex)
                {
                    Logger.Instance.Debug(TAG, $"TesterPresent error: {ex.Message}");
                }
            }, null, 
            VehicleConfig.TESTER_PRESENT_INTERVAL_MS, 
            VehicleConfig.TESTER_PRESENT_INTERVAL_MS);
        }

        private void StopTesterPresent()
        {
            _testerPresentTimer?.Dispose();
            _testerPresentTimer = null;
        }

        #endregion

        #region Security Access

        /// <summary>
        /// Perform security access with Hyundai algorithm
        /// </summary>
        public UdsResponse SecurityAccess(byte level = 0x01)
        {
            Logger.Instance.Security(TAG, $"Security access level 0x{level:X2} on {_ecu.Name}");

            // Ensure extended session
            if (_currentSession == UDS.DSC_DEFAULT_SESSION)
            {
                var sessResp = StartExtendedSession();
                if (!sessResp.IsPositive)
                    return sessResp;
            }

            // Request seed
            byte requestSeed = (byte)(level | 0x01);  // Odd = request
            byte sendKey = (byte)(level | 0x02);      // Even = send key

            var seedResponse = SendRequest(new[] { UDS.SECURITY_ACCESS, requestSeed });
            
            if (!seedResponse.IsPositive)
            {
                Logger.Instance.Security(TAG, $"Seed request failed: {seedResponse.ErrorMessage}");
                return seedResponse;
            }

            if (seedResponse.Data.Length < 3)
            {
                return UdsResponse.Error("Invalid seed response length");
            }

            // Extract seed (skip SID and sub-function)
            var seed = new byte[seedResponse.Data.Length - 2];
            Array.Copy(seedResponse.Data, 2, seed, 0, seed.Length);

            // Check for zero seed (already unlocked)
            if (seed.All(b => b == 0))
            {
                Logger.Instance.Security(TAG, "ECU already unlocked (zero seed)");
                _securityUnlocked = true;
                return UdsResponse.Positive(seedResponse.Data);
            }

            Logger.Instance.Security(TAG, $"Received seed: {BitConverter.ToString(seed)}");

            // Calculate key using Hyundai algorithm
            var key = HyundaiSecurityAlgorithm.CalculateKey(seed, level, _ecu.Name);
            
            if (key == null)
            {
                return UdsResponse.Error("Key calculation failed");
            }

            Logger.Instance.Security(TAG, $"Calculated key: {BitConverter.ToString(key)}");

            // Send key
            var keyRequest = new byte[2 + key.Length];
            keyRequest[0] = UDS.SECURITY_ACCESS;
            keyRequest[1] = sendKey;
            Array.Copy(key, 0, keyRequest, 2, key.Length);

            Thread.Sleep(VehicleConfig.SECURITY_TIMING_MS);

            var keyResponse = SendRequest(keyRequest);

            if (keyResponse.IsPositive)
            {
                _securityUnlocked = true;
                Logger.Instance.Security(TAG, "Security access GRANTED");
            }
            else
            {
                Logger.Instance.Security(TAG, $"Security access DENIED: {keyResponse.ErrorMessage}");
            }

            return keyResponse;
        }

        #endregion

        #region Read Data By Identifier

        public UdsResponse ReadDataByIdentifier(ushort did)
        {
            var request = new byte[] 
            { 
                UDS.READ_DATA_BY_ID, 
                (byte)(did >> 8), 
                (byte)(did & 0xFF) 
            };
            
            return SendRequest(request);
        }

        public UdsResponse ReadDataByIdentifier(params ushort[] dids)
        {
            if (dids == null || dids.Length == 0)
                return UdsResponse.Error("No DIDs specified");

            var request = new byte[1 + dids.Length * 2];
            request[0] = UDS.READ_DATA_BY_ID;
            
            for (int i = 0; i < dids.Length; i++)
            {
                request[1 + i * 2] = (byte)(dids[i] >> 8);
                request[2 + i * 2] = (byte)(dids[i] & 0xFF);
            }

            return SendRequest(request);
        }

        public string ReadVin()
        {
            var response = ReadDataByIdentifier(HyundaiDID.VIN);
            
            if (response.IsPositive && response.Data.Length > 3)
            {
                // Skip SID + DID
                return Encoding.ASCII.GetString(response.Data, 3, response.Data.Length - 3)
                    .Trim('\0', ' ');
            }

            // Try alternative location
            response = ReadDataByIdentifier(HyundaiDID.HMC_VIN);
            if (response.IsPositive && response.Data.Length > 3)
            {
                return Encoding.ASCII.GetString(response.Data, 3, response.Data.Length - 3)
                    .Trim('\0', ' ');
            }

            return null;
        }

        public (string partNumber, string softwareVersion, string hardwareVersion) ReadEcuInfo()
        {
            string partNumber = null, swVer = null, hwVer = null;

            var resp = ReadDataByIdentifier(HyundaiDID.VEHICLE_MANUFACTURER_SPARE_PART_NUMBER);
            if (resp.IsPositive && resp.Data.Length > 3)
                partNumber = Encoding.ASCII.GetString(resp.Data, 3, resp.Data.Length - 3).Trim();

            resp = ReadDataByIdentifier(HyundaiDID.VEHICLE_MANUFACTURER_ECU_SOFTWARE_VERSION);
            if (resp.IsPositive && resp.Data.Length > 3)
                swVer = Encoding.ASCII.GetString(resp.Data, 3, resp.Data.Length - 3).Trim();

            resp = ReadDataByIdentifier(HyundaiDID.ECU_HARDWARE_VERSION);
            if (resp.IsPositive && resp.Data.Length > 3)
                hwVer = Encoding.ASCII.GetString(resp.Data, 3, resp.Data.Length - 3).Trim();

            return (partNumber, swVer, hwVer);
        }

        #endregion

        #region Write Data By Identifier

        public UdsResponse WriteDataByIdentifier(ushort did, byte[] data)
        {
            if (data == null)
                return UdsResponse.Error("No data provided");

            var request = new byte[3 + data.Length];
            request[0] = UDS.WRITE_DATA_BY_ID;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            Array.Copy(data, 0, request, 3, data.Length);

            return SendRequest(request);
        }

        #endregion

        #region Input/Output Control

        public UdsResponse IoControl(ushort did, byte controlType, byte[] controlState = null)
        {
            int len = 4 + (controlState?.Length ?? 0);
            var request = new byte[len];
            
            request[0] = UDS.IO_CONTROL_BY_ID;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            request[3] = controlType;
            
            if (controlState != null)
                Array.Copy(controlState, 0, request, 4, controlState.Length);

            return SendRequest(request, VehicleConfig.EXTENDED_TIMEOUT_MS);
        }

        public UdsResponse IoControlReturnToEcu(ushort did) =>
            IoControl(did, UDS.IOC_RETURN_CONTROL);

        public UdsResponse IoControlFreeze(ushort did) =>
            IoControl(did, UDS.IOC_FREEZE_CURRENT);

        public UdsResponse IoControlShortTerm(ushort did, byte[] value) =>
            IoControl(did, UDS.IOC_SHORT_TERM_ADJ, value);

        #endregion

        #region Routine Control

        public UdsResponse StartRoutine(ushort rid, byte[] options = null)
        {
            int len = 4 + (options?.Length ?? 0);
            var request = new byte[len];
            
            request[0] = UDS.ROUTINE_CONTROL;
            request[1] = UDS.RC_START_ROUTINE;
            request[2] = (byte)(rid >> 8);
            request[3] = (byte)(rid & 0xFF);
            
            if (options != null)
                Array.Copy(options, 0, request, 4, options.Length);

            return SendRequest(request, VehicleConfig.EXTENDED_TIMEOUT_MS);
        }

        public UdsResponse StopRoutine(ushort rid)
        {
            var request = new byte[]
            {
                UDS.ROUTINE_CONTROL,
                UDS.RC_STOP_ROUTINE,
                (byte)(rid >> 8),
                (byte)(rid & 0xFF)
            };

            return SendRequest(request);
        }

        public UdsResponse RequestRoutineResults(ushort rid)
        {
            var request = new byte[]
            {
                UDS.ROUTINE_CONTROL,
                UDS.RC_REQUEST_RESULTS,
                (byte)(rid >> 8),
                (byte)(rid & 0xFF)
            };

            return SendRequest(request);
        }

        #endregion

        #region DTC Operations

        public UdsResponse ClearDtc(uint dtcGroup = 0xFFFFFF)
        {
            var request = new byte[]
            {
                UDS.CLEAR_DTC,
                (byte)((dtcGroup >> 16) & 0xFF),
                (byte)((dtcGroup >> 8) & 0xFF),
                (byte)(dtcGroup & 0xFF)
            };

            return SendRequest(request, VehicleConfig.EXTENDED_TIMEOUT_MS);
        }

        public UdsResponse ReadDtcByStatus(byte statusMask = 0xFF)
        {
            var request = new byte[]
            {
                UDS.READ_DTC_INFO,
                UDS.RDTC_REPORT_BY_STATUS,
                statusMask
            };

            return SendRequest(request);
        }

        public UdsResponse ReadDtcCount(byte statusMask = 0xFF)
        {
            var request = new byte[]
            {
                UDS.READ_DTC_INFO,
                UDS.RDTC_REPORT_NUMBER,
                statusMask
            };

            return SendRequest(request);
        }

        public UdsResponse ReadSupportedDtc()
        {
            var request = new byte[]
            {
                UDS.READ_DTC_INFO,
                UDS.RDTC_REPORT_SUPPORTED
            };

            return SendRequest(request);
        }

        public UdsResponse ReadDtcSnapshot(uint dtc, byte recordNumber)
        {
            var request = new byte[]
            {
                UDS.READ_DTC_INFO,
                UDS.RDTC_REPORT_SNAPSHOT_DATA,
                (byte)((dtc >> 16) & 0xFF),
                (byte)((dtc >> 8) & 0xFF),
                (byte)(dtc & 0xFF),
                recordNumber
            };

            return SendRequest(request);
        }

        public UdsResponse ReadDtcExtendedData(uint dtc, byte recordNumber)
        {
            var request = new byte[]
            {
                UDS.READ_DTC_INFO,
                UDS.RDTC_REPORT_EXTENDED,
                (byte)((dtc >> 16) & 0xFF),
                (byte)((dtc >> 8) & 0xFF),
                (byte)(dtc & 0xFF),
                recordNumber
            };

            return SendRequest(request);
        }

        #endregion

        #region ECU Reset

        public UdsResponse EcuReset(byte resetType = UDS.RESET_SOFT)
        {
            StopTesterPresent();
            
            var request = new byte[] { UDS.ECU_RESET, resetType };
            var response = SendRequest(request);

            if (response.IsPositive)
            {
                _currentSession = UDS.DSC_DEFAULT_SESSION;
                _securityUnlocked = false;
            }

            return response;
        }

        public UdsResponse HardReset() => EcuReset(UDS.RESET_HARD);
        public UdsResponse SoftReset() => EcuReset(UDS.RESET_SOFT);
        public UdsResponse KeyOffOnReset() => EcuReset(UDS.RESET_KEY_OFF_ON);

        #endregion

        #region Communication Control

        public UdsResponse CommunicationControl(byte controlType, byte communicationType = 0x01)
        {
            var request = new byte[]
            {
                UDS.COMMUNICATION_CONTROL,
                controlType,
                communicationType
            };

            return SendRequest(request);
        }

        public UdsResponse EnableCommunication() =>
            CommunicationControl(UDS.CC_ENABLE_RX_TX);

        public UdsResponse DisableCommunication() =>
            CommunicationControl(UDS.CC_DISABLE_RX_TX);

        #endregion

        #region DTC Setting Control

        public UdsResponse ControlDtcSetting(bool enable)
        {
            var request = new byte[]
            {
                UDS.CONTROL_DTC_SETTING,
                enable ? UDS.DTC_SETTING_ON : UDS.DTC_SETTING_OFF
            };

            return SendRequest(request);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            StopTesterPresent();
            _sessionActive = false;
        }
    }

    /// <summary>
    /// UDS response wrapper with status and data
    /// </summary>
    public class UdsResponse
    {
        public bool IsPositive { get; private set; }
        public bool IsNegative { get; private set; }
        public bool IsTimeout { get; private set; }
        public bool HasError { get; private set; }
        
        public byte[] Data { get; private set; }
        public byte ServiceId { get; private set; }
        public byte NegativeResponseCode { get; private set; }
        public string ErrorMessage { get; private set; }

        public string NrcDescription => 
            IsNegative ? NRC.GetDescription(NegativeResponseCode) : null;

        private UdsResponse() { }

        public static UdsResponse Positive(byte[] data) => new UdsResponse
        {
            IsPositive = true,
            Data = data,
            ServiceId = data?.Length > 0 ? data[0] : (byte)0
        };

        public static UdsResponse Negative(byte sid, byte nrc) => new UdsResponse
        {
            IsNegative = true,
            ServiceId = sid,
            NegativeResponseCode = nrc,
            ErrorMessage = $"NRC 0x{nrc:X2}: {NRC.GetDescription(nrc)}",
            Data = new[] { (byte)0x7F, sid, nrc }
        };

        public static UdsResponse Timeout() => new UdsResponse
        {
            IsTimeout = true,
            ErrorMessage = "No response from ECU (timeout)"
        };

        public static UdsResponse Error(string message) => new UdsResponse
        {
            HasError = true,
            ErrorMessage = message
        };

        public override string ToString()
        {
            if (IsPositive)
                return $"Positive: {BitConverter.ToString(Data ?? Array.Empty<byte>())}";
            if (IsNegative)
                return $"Negative: SID=0x{ServiceId:X2}, NRC=0x{NegativeResponseCode:X2} ({NrcDescription})";
            if (IsTimeout)
                return "Timeout";
            return $"Error: {ErrorMessage}";
        }
    }

    #endregion

    #region Hyundai Security Algorithm

    /// <summary>
    /// Hyundai/Kia security access key calculation algorithms
    /// </summary>
    public static class HyundaiSecurityAlgorithm
    {
        private const string TAG = "Security";

        // Known algorithm variants by ECU type
        private enum AlgorithmType
        {
            Standard,       // Most ECUs
            Kefico,         // Engine ECU (ME17.9.11)
            Bosch,          // Diesel variants
            Continental,    // Body electronics
            Mando           // Chassis systems
        }

        /// <summary>
        /// Calculate security key from seed for Hyundai ECUs
        /// </summary>
        public static byte[] CalculateKey(byte[] seed, byte level, string ecuName)
        {
            if (seed == null || seed.Length == 0)
                return null;

            // Determine algorithm based on ECU and level
            var algorithm = DetermineAlgorithm(ecuName, level);
            
            Logger.Instance.Security(TAG, 
                $"Using {algorithm} algorithm for {ecuName} level 0x{level:X2}");

            return algorithm switch
            {
                AlgorithmType.Kefico => CalculateKeficoKey(seed, level),
                AlgorithmType.Bosch => CalculateBoschKey(seed, level),
                AlgorithmType.Continental => CalculateContinentalKey(seed, level),
                AlgorithmType.Mando => CalculateMandoKey(seed, level),
                _ => CalculateStandardKey(seed, level)
            };
        }

        private static AlgorithmType DetermineAlgorithm(string ecuName, byte level)
        {
            // ECU-specific algorithm selection
            if (ecuName.Contains("ECM") || ecuName.Contains("Engine"))
                return AlgorithmType.Kefico;
            
            if (ecuName.Contains("ABS") || ecuName.Contains("ESP") || ecuName.Contains("MDPS"))
                return AlgorithmType.Mando;
                
            if (ecuName.Contains("BCM") || ecuName.Contains("Smart"))
                return AlgorithmType.Continental;

            return AlgorithmType.Standard;
        }

        /// <summary>
        /// Standard Hyundai/Kia algorithm (most common)
        /// </summary>
        private static byte[] CalculateStandardKey(byte[] seed, byte level)
        {
            // 4-byte seed/key
            if (seed.Length == 4)
            {
                uint s = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);
                uint key = StandardAlgorithm4(s, level);
                
                return new byte[]
                {
                    (byte)((key >> 24) & 0xFF),
                    (byte)((key >> 16) & 0xFF),
                    (byte)((key >> 8) & 0xFF),
                    (byte)(key & 0xFF)
                };
            }
            
            // 2-byte seed/key
            if (seed.Length == 2)
            {
                ushort s = (ushort)((seed[0] << 8) | seed[1]);
                ushort key = StandardAlgorithm2(s, level);
                
                return new byte[]
                {
                    (byte)((key >> 8) & 0xFF),
                    (byte)(key & 0xFF)
                };
            }

            return null;
        }

        private static uint StandardAlgorithm4(uint seed, byte level)
        {
            // Hyundai standard 4-byte algorithm
            uint[] constants = level switch
            {
                0x01 => new uint[] { 0xC541A9, 0x8A6231, 0x4D5B2C, 0xF1E3A7 },
                0x03 => new uint[] { 0xA3B7C9, 0x5E7F1D, 0x2C4D6E, 0x9F8A7B },
                0x11 => new uint[] { 0xD8E9FA, 0x3B4C5D, 0x7E8F9A, 0x1A2B3C },
                _ => new uint[] { 0x12345678, 0x9ABCDEF0, 0x0FEDCBA9, 0x87654321 }
            };

            uint key = seed;
            
            for (int i = 0; i < 32; i++)
            {
                if ((key & 1) != 0)
                {
                    key = (key >> 1) ^ constants[i % 4];
                }
                else
                {
                    key = key >> 1;
                }
                
                key ^= (uint)(constants[(i + 1) % 4] >> (i % 8));
            }

            return key;
        }

        private static ushort StandardAlgorithm2(ushort seed, byte level)
        {
            ushort key = seed;
            ushort constant = level switch
            {
                0x01 => (ushort)0x8F5A,
                0x03 => (ushort)0xC3E7,
                _ => (ushort)0x5A3C
            };

            for (int i = 0; i < 16; i++)
            {
                if ((key & 1) != 0)
                {
                    key = (ushort)((key >> 1) ^ constant);
                }
                else
                {
                    key = (ushort)(key >> 1);
                }
            }

            return key;
        }

        /// <summary>
        /// Kefico ME17.9.11 algorithm (Gamma engine ECU)
        /// </summary>
        private static byte[] CalculateKeficoKey(byte[] seed, byte level)
        {
            if (seed.Length != 4)
                return CalculateStandardKey(seed, level);

            uint s = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);
            
            // Kefico-specific constants for Gamma 1.4
            uint c1 = 0x7A6C5B;
            uint c2 = 0xF3E2D1;
            uint c3 = 0x1D2E3F;

            uint key = s;
            
            // XOR with level-dependent value
            key ^= (uint)(level * 0x10101);
            
            // Bit manipulation
            for (int round = 0; round < 4; round++)
            {
                uint temp = key;
                
                key = ((key << 5) | (key >> 27));
                key ^= c1;
                key = ((key >> 3) | (key << 29));
                key += c2;
                key ^= temp;
                key = ((key << 11) | (key >> 21));
                key ^= c3;
            }

            // Final transformation
            key = ~key;
            key = ((key & 0xFFFF) << 16) | ((key >> 16) & 0xFFFF);

            return new byte[]
            {
                (byte)((key >> 24) & 0xFF),
                (byte)((key >> 16) & 0xFF),
                (byte)((key >> 8) & 0xFF),
                (byte)(key & 0xFF)
            };
        }

        /// <summary>
        /// Bosch algorithm (diesel ECUs)
        /// </summary>
        private static byte[] CalculateBoschKey(byte[] seed, byte level)
        {
            if (seed.Length != 4)
                return CalculateStandardKey(seed, level);

            uint s = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);
            
            // Bosch EDC17 algorithm
            uint key = s;
            uint mask = 0xD9A7C581;

            for (int i = 0; i < 35; i++)
            {
                if ((key & 0x80000000) != 0)
                {
                    key = (key << 1) ^ mask;
                }
                else
                {
                    key = key << 1;
                }
            }

            key ^= (uint)(level << 16 | level << 8 | level);

            return new byte[]
            {
                (byte)((key >> 24) & 0xFF),
                (byte)((key >> 16) & 0xFF),
                (byte)((key >> 8) & 0xFF),
                (byte)(key & 0xFF)
            };
        }

        /// <summary>
        /// Continental algorithm (body electronics)
        /// </summary>
        private static byte[] CalculateContinentalKey(byte[] seed, byte level)
        {
            if (seed.Length != 4)
                return CalculateStandardKey(seed, level);

            uint s = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);
            
            uint key = s;
            
        // Continental VDO algorithm
        key = (key ^ 0x5C9E3A1F);
        key = ((key >> 16) & 0xFFFF) | ((key & 0xFFFF) << 16);
        key ^= (uint)(level * 0x0101);
            
            for (int i = 0; i < 8; i++)
            {
                uint bit = key & 1;
                key = key >> 1;
                if (bit != 0)
                    key ^= 0xA1B2C3D4;
            }

            return new byte[]
            {
                (byte)((key >> 24) & 0xFF),
                (byte)((key >> 16) & 0xFF),
                (byte)((key >> 8) & 0xFF),
                (byte)(key & 0xFF)
            };
        }

        /// <summary>
        /// Mando algorithm (ABS/ESP/MDPS)
        /// </summary>
        private static byte[] CalculateMandoKey(byte[] seed, byte level)
        {
            if (seed.Length != 4)
                return CalculateStandardKey(seed, level);

            uint s = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);
            
            // Mando chassis module algorithm
            uint key = s;
            uint xorVal = 0x3E7C9A5B;

            key = ((key & 0xFF00FF00) >> 8) | ((key & 0x00FF00FF) << 8);
            
            for (int i = 0; i < 4; i++)
            {
                key ^= xorVal;
                key = (key << 7) | (key >> 25);
                xorVal = (xorVal >> 3) | (xorVal << 29);
            }

            key ^= (uint)(level * 0x11111111);

            return new byte[]
            {
                (byte)((key >> 24) & 0xFF),
                (byte)((key >> 16) & 0xFF),
                (byte)((key >> 8) & 0xFF),
                (byte)(key & 0xFF)
            };
        }
    }

    #endregion

    #region OBD-II Standard Protocol

    /// <summary>
    /// OBD-II (SAE J1979) protocol implementation for standard PIDs
    /// </summary>
    public class ObdIIClient
    {
        private const string TAG = "OBD-II";

        private readonly J2534Api _api;
        private readonly uint _channelId;
        private readonly IsoTpHandler _transport;
        
        private static readonly Dictionary<byte, string> ModeDescriptions = new()
        {
            [0x01] = "Current Data",
            [0x02] = "Freeze Frame",
            [0x03] = "Stored DTCs",
            [0x04] = "Clear DTCs",
            [0x05] = "O2 Sensor Monitoring",
            [0x06] = "On-board Monitoring",
            [0x07] = "Pending DTCs",
            [0x08] = "Control Operation",
            [0x09] = "Vehicle Information",
            [0x0A] = "Permanent DTCs"
        };

        public ObdIIClient(J2534Api api, uint channelId)
        {
            _api = api;
            _channelId = channelId;
            _transport = new IsoTpHandler(api, channelId, 
                VehicleConfig.OBD_BROADCAST_ID, VehicleConfig.ECM_RESPONSE_ID);
        }

        /// <summary>
        /// Query OBD-II Mode 01 PID
        /// </summary>
        public byte[] QueryMode01(byte pid, int timeoutMs = 1000)
        {
            var request = new byte[] { 0x01, pid };
            return _transport.SendAndReceive(request, timeoutMs);
        }

        /// <summary>
        /// Read supported PIDs for Mode 01
        /// </summary>
        public HashSet<byte> GetSupportedPids()
        {
            var supported = new HashSet<byte>();
            
            // Query PID support bitmaps at 0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0
            byte[] supportPids = { 0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0 };

            foreach (var supportPid in supportPids)
            {
                var response = QueryMode01(supportPid);
                
                if (response != null && response.Length >= 6)
                {
                    // Response: 41 [PID] [4 bytes bitmap]
                    uint bitmap = (uint)((response[2] << 24) | (response[3] << 16) | 
                                        (response[4] << 8) | response[5]);

                    for (int i = 0; i < 32; i++)
                    {
                        if ((bitmap & (1u << (31 - i))) != 0)
                        {
                            byte pid = (byte)(supportPid + i + 1);
                            supported.Add(pid);
                        }
                    }

                    // Stop if next support PID not indicated
                    if ((bitmap & 1) == 0)
                        break;
                }
                else
                {
                    break;
                }
            }

            Logger.Instance.Info(TAG, $"Found {supported.Count} supported PIDs");
            return supported;
        }

        /// <summary>
        /// Read VIN via Mode 09 PID 02
        /// </summary>
        public string ReadVin()
        {
            var response = _transport.SendAndReceive(new byte[] { 0x09, 0x02 }, 2000);
            
            if (response != null && response.Length > 3)
            {
                // Skip: 49 02 [count] [VIN bytes]
                int offset = 3;
                if (response[0] == 0x49 && response[1] == 0x02)
                {
                    return Encoding.ASCII.GetString(response, offset, response.Length - offset)
                        .Trim('\0', ' ');
                }
            }

            return null;
        }

        /// <summary>
        /// Read current DTCs (Mode 03)
        /// </summary>
        public List<string> ReadCurrentDtcs()
        {
            var dtcs = new List<string>();
            var response = _transport.SendAndReceive(new byte[] { 0x03 }, 2000);

            if (response != null && response.Length >= 2 && response[0] == 0x43)
            {
                int count = response[1];
                
                for (int i = 0; i < count && (2 + i * 2 + 1) < response.Length; i++)
                {
                    byte b1 = response[2 + i * 2];
                    byte b2 = response[3 + i * 2];
                    
                    if (b1 != 0 || b2 != 0)
                    {
                        string dtc = DecodeDtc(b1, b2);
                        dtcs.Add(dtc);
                    }
                }
            }

            return dtcs;
        }

        /// <summary>
        /// Read pending DTCs (Mode 07)
        /// </summary>
        public List<string> ReadPendingDtcs()
        {
            var dtcs = new List<string>();
            var response = _transport.SendAndReceive(new byte[] { 0x07 }, 2000);

            if (response != null && response.Length >= 2 && response[0] == 0x47)
            {
                int count = response[1];
                
                for (int i = 0; i < count && (2 + i * 2 + 1) < response.Length; i++)
                {
                    byte b1 = response[2 + i * 2];
                    byte b2 = response[3 + i * 2];
                    
                    if (b1 != 0 || b2 != 0)
                    {
                        string dtc = DecodeDtc(b1, b2);
                        dtcs.Add(dtc);
                    }
                }
            }

            return dtcs;
        }

        /// <summary>
        /// Clear DTCs (Mode 04)
        /// </summary>
        public bool ClearDtcs()
        {
            var response = _transport.SendAndReceive(new byte[] { 0x04 }, 5000);
            return response != null && response.Length > 0 && response[0] == 0x44;
        }

        /// <summary>
        /// Decode 2-byte DTC to standard format (e.g., P0123)
        /// </summary>
        private string DecodeDtc(byte b1, byte b2)
        {
            char type = ((b1 >> 6) & 0x03) switch
            {
                0 => 'P',  // Powertrain
                1 => 'C',  // Chassis
                2 => 'B',  // Body
                3 => 'U',  // Network
                _ => '?'
            };

            int digit1 = (b1 >> 4) & 0x03;
            int digit2 = b1 & 0x0F;
            int digit3 = (b2 >> 4) & 0x0F;
            int digit4 = b2 & 0x0F;

            return $"{type}{digit1}{digit2:X}{digit3:X}{digit4:X}";
        }

        #region Live Data Reading

        public double? ReadEngineRpm()
        {
            var response = QueryMode01(0x0C);
            if (response != null && response.Length >= 4)
            {
                return ((response[2] * 256) + response[3]) / 4.0;
            }
            return null;
        }

        public double? ReadVehicleSpeed()
        {
            var response = QueryMode01(0x0D);
            if (response != null && response.Length >= 3)
            {
                return response[2];
            }
            return null;
        }

        public double? ReadCoolantTemp()
        {
            var response = QueryMode01(0x05);
            if (response != null && response.Length >= 3)
            {
                return response[2] - 40;
            }
            return null;
        }

        public double? ReadIntakeTemp()
        {
            var response = QueryMode01(0x0F);
            if (response != null && response.Length >= 3)
            {
                return response[2] - 40;
            }
            return null;
        }

        public double? ReadEngineLoad()
        {
            var response = QueryMode01(0x04);
            if (response != null && response.Length >= 3)
            {
                return response[2] * 100.0 / 255.0;
            }
            return null;
        }

        public double? ReadThrottlePosition()
        {
            var response = QueryMode01(0x11);
            if (response != null && response.Length >= 3)
            {
                return response[2] * 100.0 / 255.0;
            }
            return null;
        }

        public double? ReadMaf()
        {
            var response = QueryMode01(0x10);
            if (response != null && response.Length >= 4)
            {
                return ((response[2] * 256) + response[3]) / 100.0;
            }
            return null;
        }

        public double? ReadFuelPressure()
        {
            var response = QueryMode01(0x0A);
            if (response != null && response.Length >= 3)
            {
                return response[2] * 3;
            }
            return null;
        }

        public double? ReadTimingAdvance()
        {
            var response = QueryMode01(0x0E);
            if (response != null && response.Length >= 3)
            {
                return (response[2] / 2.0) - 64;
            }
            return null;
        }

        public double? ReadShortTermFuelTrim(int bank = 1)
        {
            byte pid = bank == 1 ? (byte)0x06 : (byte)0x08;
            var response = QueryMode01(pid);
            if (response != null && response.Length >= 3)
            {
                return (response[2] - 128) * 100.0 / 128.0;
            }
            return null;
        }

        public double? ReadLongTermFuelTrim(int bank = 1)
        {
            byte pid = bank == 1 ? (byte)0x07 : (byte)0x09;
            var response = QueryMode01(pid);
            if (response != null && response.Length >= 3)
            {
                return (response[2] - 128) * 100.0 / 128.0;
            }
            return null;
        }

        public (bool mil, int dtcCount)? ReadMilStatus()
        {
            var response = QueryMode01(0x01);
            if (response != null && response.Length >= 6)
            {
                bool mil = (response[2] & 0x80) != 0;
                int count = response[2] & 0x7F;
                return (mil, count);
            }
            return null;
        }

        public double? ReadO2SensorVoltage(int sensor)
        {
            if (sensor < 1 || sensor > 8) return null;
            byte pid = (byte)(0x13 + sensor);
            
            var response = QueryMode01(pid);
            if (response != null && response.Length >= 3)
            {
                return response[2] / 200.0;
            }
            return null;
        }

        public double? ReadCatalystTemp(int bank, int sensor)
        {
            byte pid = (bank, sensor) switch
            {
                (1, 1) => 0x3C,
                (2, 1) => 0x3D,
                (1, 2) => 0x3E,
                (2, 2) => 0x3F,
                _ => 0
            };

            if (pid == 0) return null;

            var response = QueryMode01(pid);
            if (response != null && response.Length >= 4)
            {
                return (((response[2] * 256) + response[3]) / 10.0) - 40;
            }
            return null;
        }

        public double? ReadControlModuleVoltage()
        {
            var response = QueryMode01(0x42);
            if (response != null && response.Length >= 4)
            {
                return ((response[2] * 256) + response[3]) / 1000.0;
            }
            return null;
        }

        public double? ReadAbsoluteLoad()
        {
            var response = QueryMode01(0x43);
            if (response != null && response.Length >= 4)
            {
                return ((response[2] * 256) + response[3]) * 100.0 / 255.0;
            }
            return null;
        }

        public double? ReadAmbientAirTemp()
        {
            var response = QueryMode01(0x46);
            if (response != null && response.Length >= 3)
            {
                return response[2] - 40;
            }
            return null;
        }

        public int? ReadRuntimeSinceStart()
        {
            var response = QueryMode01(0x1F);
            if (response != null && response.Length >= 4)
            {
                return (response[2] * 256) + response[3];
            }
            return null;
        }

        public int? ReadDistanceWithMil()
        {
            var response = QueryMode01(0x21);
            if (response != null && response.Length >= 4)
            {
                return (response[2] * 256) + response[3];
            }
            return null;
        }

        #endregion
    }

    #endregion

    #region DTC Definitions

    /// <summary>
    /// Parsed DTC information
    /// </summary>
    public class DiagnosticTroubleCode
    {
        public uint RawCode { get; set; }
        public string Code { get; set; }           // e.g., "P0300"
        public string Description { get; set; }
        public byte StatusMask { get; set; }
        public string EcuName { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public bool IsPending { get; set; }
        public bool IsActive { get; set; }
        public bool IsStored { get; set; }
        public bool IsPermanent { get; set; }
        public DateTime? FirstOccurrence { get; set; }
        public int OccurrenceCount { get; set; }
        public Dictionary<string, string> FreezeFrameData { get; set; } = new();

        public override string ToString() => 
            $"{Code}: {Description ?? "Unknown"} [{(IsActive ? "Active" : IsPending ? "Pending" : "Stored")}]";
    }

    /// <summary>
    /// Hyundai i30 DTC database
    /// </summary>
    public static class HyundaiDtcDatabase
    {
        private static readonly Dictionary<uint, (string Code, string Description, string Category)> _dtcs = new()
        {
            // Engine/Powertrain (P0xxx)
            [0x000300] = ("P0300", "Random/Multiple Cylinder Misfire Detected", "Engine"),
            [0x000301] = ("P0301", "Cylinder 1 Misfire Detected", "Engine"),
            [0x000302] = ("P0302", "Cylinder 2 Misfire Detected", "Engine"),
            [0x000303] = ("P0303", "Cylinder 3 Misfire Detected", "Engine"),
            [0x000304] = ("P0304", "Cylinder 4 Misfire Detected", "Engine"),
            [0x000100] = ("P0100", "Mass Air Flow Circuit Malfunction", "Fuel/Air"),
            [0x000101] = ("P0101", "Mass Air Flow Circuit Range/Performance", "Fuel/Air"),
            [0x000102] = ("P0102", "Mass Air Flow Circuit Low Input", "Fuel/Air"),
            [0x000103] = ("P0103", "Mass Air Flow Circuit High Input", "Fuel/Air"),
            [0x000110] = ("P0110", "Intake Air Temperature Circuit Malfunction", "Fuel/Air"),
            [0x000115] = ("P0115", "Engine Coolant Temperature Circuit Malfunction", "Cooling"),
            [0x000116] = ("P0116", "Engine Coolant Temperature Circuit Range/Performance", "Cooling"),
            [0x000117] = ("P0117", "Engine Coolant Temperature Circuit Low", "Cooling"),
            [0x000118] = ("P0118", "Engine Coolant Temperature Circuit High", "Cooling"),
            [0x000120] = ("P0120", "Throttle Position Sensor Circuit Malfunction", "Fuel/Air"),
            [0x000121] = ("P0121", "Throttle Position Sensor Range/Performance", "Fuel/Air"),
            [0x000122] = ("P0122", "Throttle Position Sensor Circuit Low", "Fuel/Air"),
            [0x000123] = ("P0123", "Throttle Position Sensor Circuit High", "Fuel/Air"),
            [0x000130] = ("P0130", "O2 Sensor Circuit Bank 1 Sensor 1", "Emissions"),
            [0x000131] = ("P0131", "O2 Sensor Circuit Low Voltage B1S1", "Emissions"),
            [0x000132] = ("P0132", "O2 Sensor Circuit High Voltage B1S1", "Emissions"),
            [0x000133] = ("P0133", "O2 Sensor Circuit Slow Response B1S1", "Emissions"),
            [0x000134] = ("P0134", "O2 Sensor Circuit No Activity B1S1", "Emissions"),
            [0x000135] = ("P0135", "O2 Sensor Heater Circuit B1S1", "Emissions"),
            [0x000136] = ("P0136", "O2 Sensor Circuit Bank 1 Sensor 2", "Emissions"),
            [0x000170] = ("P0170", "Fuel Trim Malfunction Bank 1", "Fuel/Air"),
            [0x000171] = ("P0171", "System Too Lean Bank 1", "Fuel/Air"),
            [0x000172] = ("P0172", "System Too Rich Bank 1", "Fuel/Air"),
            [0x000200] = ("P0200", "Injector Circuit Malfunction", "Fuel Delivery"),
            [0x000201] = ("P0201", "Injector Circuit Malfunction Cylinder 1", "Fuel Delivery"),
            [0x000202] = ("P0202", "Injector Circuit Malfunction Cylinder 2", "Fuel Delivery"),
            [0x000203] = ("P0203", "Injector Circuit Malfunction Cylinder 3", "Fuel Delivery"),
            [0x000204] = ("P0204", "Injector Circuit Malfunction Cylinder 4", "Fuel Delivery"),
            [0x000325] = ("P0325", "Knock Sensor 1 Circuit Malfunction", "Engine"),
            [0x000335] = ("P0335", "Crankshaft Position Sensor Circuit", "Engine"),
            [0x000336] = ("P0336", "Crankshaft Position Sensor Range/Performance", "Engine"),
            [0x000340] = ("P0340", "Camshaft Position Sensor Circuit", "Engine"),
            [0x000400] = ("P0400", "EGR Flow Malfunction", "Emissions"),
            [0x000401] = ("P0401", "EGR Flow Insufficient", "Emissions"),
            [0x000420] = ("P0420", "Catalyst System Efficiency Below Threshold B1", "Emissions"),
            [0x000440] = ("P0440", "EVAP System Malfunction", "Emissions"),
            [0x000441] = ("P0441", "EVAP System Incorrect Purge Flow", "Emissions"),
            [0x000442] = ("P0442", "EVAP System Small Leak Detected", "Emissions"),
            [0x000443] = ("P0443", "EVAP Purge Control Valve Circuit", "Emissions"),
            [0x000446] = ("P0446", "EVAP Vent Control Circuit", "Emissions"),
            [0x000500] = ("P0500", "Vehicle Speed Sensor Malfunction", "Vehicle Speed"),
            [0x000505] = ("P0505", "Idle Control System Malfunction", "Idle Control"),
            [0x000506] = ("P0506", "Idle Control System RPM Lower Than Expected", "Idle Control"),
            [0x000507] = ("P0507", "Idle Control System RPM Higher Than Expected", "Idle Control"),
            [0x000560] = ("P0560", "System Voltage Malfunction", "Electrical"),
            [0x000562] = ("P0562", "System Voltage Low", "Electrical"),
            [0x000563] = ("P0563", "System Voltage High", "Electrical"),
            [0x000600] = ("P0600", "Serial Communication Link Malfunction", "Network"),
            [0x000601] = ("P0601", "Internal Control Module Memory Check Sum Error", "ECU"),
            [0x000700] = ("P0700", "Transmission Control System Malfunction", "Transmission"),
            [0x000715] = ("P0715", "Input/Turbine Speed Sensor Circuit", "Transmission"),
            [0x000720] = ("P0720", "Output Speed Sensor Circuit", "Transmission"),
            [0x000725] = ("P0725", "Engine Speed Input Circuit Malfunction", "Transmission"),
            [0x000730] = ("P0730", "Incorrect Gear Ratio", "Transmission"),
            
            // Chassis (C0xxx)
            [0x400000] = ("C0000", "Vehicle Speed Information Circuit", "Chassis"),
            [0x400035] = ("C0035", "Left Front Wheel Speed Circuit", "ABS"),
            [0x400040] = ("C0040", "Right Front Wheel Speed Circuit", "ABS"),
            [0x400045] = ("C0045", "Left Rear Wheel Speed Circuit", "ABS"),
            [0x400050] = ("C0050", "Right Rear Wheel Speed Circuit", "ABS"),
            [0x401200] = ("C1200", "ABS Hydraulic Pump Motor Circuit", "ABS"),
            [0x401201] = ("C1201", "Engine Control System Malfunction", "ESC"),
            [0x401241] = ("C1241", "Low Battery Positive Voltage", "ABS"),
            [0x401611] = ("C1611", "MDPS Motor Current Sensor", "Steering"),
            [0x401612] = ("C1612", "MDPS Torque Sensor", "Steering"),
            [0x401613] = ("C1613", "MDPS Steering Angle Sensor", "Steering"),

            // Body (B0xxx)
            [0x800000] = ("B0000", "PCM Discrete Input Speed Signal Error", "Body"),
            [0x801000] = ("B1000", "ECU Malfunction", "Body"),
            [0x801121] = ("B1121", "Driver Airbag Circuit Short to Ground", "SRS"),
            [0x801122] = ("B1122", "Driver Airbag Circuit Short to Battery", "SRS"),
            [0x801131] = ("B1131", "Passenger Airbag Circuit Short to Ground", "SRS"),
            [0x801132] = ("B1132", "Passenger Airbag Circuit Short to Battery", "SRS"),
            [0x801200] = ("B1200", "Climate Control Push Button Circuit", "HVAC"),
            [0x801600] = ("B1600", "PATS Received Invalid Key Code", "Security"),
            [0x802230] = ("B2230", "Brake Fluid Level Switch Circuit Low", "Body"),
            [0x802290] = ("B2290", "Wiper Low Speed Circuit", "Body"),
            [0x802291] = ("B2291", "Wiper High Speed Circuit", "Body"),

            // Network (U0xxx)
            [0xC00001] = ("U0001", "High Speed CAN Communication Bus", "Network"),
            [0xC00002] = ("U0002", "High Speed CAN Communication Bus Performance", "Network"),
            [0xC00100] = ("U0100", "Lost Communication With ECM/PCM", "Network"),
            [0xC00101] = ("U0101", "Lost Communication With TCM", "Network"),
            [0xC00102] = ("U0102", "Lost Communication With Transfer Case Module", "Network"),
            [0xC00121] = ("U0121", "Lost Communication With ABS", "Network"),
            [0xC00126] = ("U0126", "Lost Communication With Steering Angle Sensor", "Network"),
            [0xC00129] = ("U0129", "Lost Communication With Brake System Module", "Network"),
            [0xC00140] = ("U0140", "Lost Communication With BCM", "Network"),
            [0xC00151] = ("U0151", "Lost Communication With SRS", "Network"),
            [0xC00155] = ("U0155", "Lost Communication With Cluster", "Network"),
            [0xC00164] = ("U0164", "Lost Communication With HVAC", "Network"),
            [0xC00184] = ("U0184", "Lost Communication With Audio", "Network"),
            [0xC00401] = ("U0401", "Invalid Data Received From ECM", "Network"),
            [0xC00402] = ("U0402", "Invalid Data Received From TCM", "Network"),
        };

        public static DiagnosticTroubleCode Lookup(uint rawCode, byte status = 0)
        {
            var dtc = new DiagnosticTroubleCode
            {
                RawCode = rawCode,
                StatusMask = status
            };

            // Decode raw code to standard format
            dtc.Code = DecodeRawDtc(rawCode);
            
            // Check database
            if (_dtcs.TryGetValue(rawCode, out var info))
            {
                dtc.Description = info.Description;
                dtc.Category = info.Category;
            }
            else
            {
                dtc.Description = "Unknown fault code";
                dtc.Category = "Unknown";
            }

            // Decode status byte (ISO 14229)
            dtc.IsActive = (status & 0x01) != 0;      // Test failed
            dtc.IsPending = (status & 0x04) != 0;     // Pending
            dtc.IsStored = (status & 0x08) != 0;      // Confirmed
            dtc.IsPermanent = (status & 0x10) != 0;   // Permanent

            // Determine severity
            dtc.Severity = dtc.Code[0] switch
            {
                'P' when dtc.Code.StartsWith("P0") => "Emissions-related",
                'P' => "Powertrain",
                'C' => "Chassis Safety",
                'B' when dtc.Category == "SRS" => "Safety Critical",
                'B' => "Body/Comfort",
                'U' => "Network Communication",
                _ => "Unknown"
            };

            return dtc;
        }

        private static string DecodeRawDtc(uint raw)
        {
            // UDS DTC format: 3 bytes
            // Byte 0: [Type(2 bits)][Digit1(2 bits)][Digit2(4 bits)]
            // Byte 1: [Digit3(4 bits)][Digit4(4 bits)]
            // Byte 2: Failure type byte (ignored for code)
            
            byte b0 = (byte)((raw >> 16) & 0xFF);
            byte b1 = (byte)((raw >> 8) & 0xFF);

            char type = ((b0 >> 6) & 0x03) switch
            {
                0 => 'P',
                1 => 'C',
                2 => 'B',
                3 => 'U',
                _ => '?'
            };

            int d1 = (b0 >> 4) & 0x03;
            int d2 = b0 & 0x0F;
            int d3 = (b1 >> 4) & 0x0F;
            int d4 = b1 & 0x0F;

            return $"{type}{d1}{d2:X}{d3:X}{d4:X}";
        }

        public static bool TryGetDescription(string code, out string description)
        {
            description = null;
            
            foreach (var kvp in _dtcs)
            {
                if (kvp.Value.Code == code)
                {
                    description = kvp.Value.Description;
                    return true;
                }
            }
            
            return false;
        }
    }

    #endregion

    #region Live Data Monitor

    /// <summary>
    /// Live data PID definition
    /// </summary>
    public class LiveDataPid
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public Func<byte[], double?> Parser { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public string Format { get; set; } = "F1";
        public bool IsSupported { get; set; } = true;
        public byte Mode { get; set; } = 0x01;
        public byte Pid { get; set; }
        public ushort? Did { get; set; }  // For UDS access
    }

    /// <summary>
    /// Real-time data monitoring with configurable update rates
    /// </summary>
    public sealed class LiveDataMonitor : IDisposable
    {
        private const string TAG = "LiveData";

        private readonly ObdIIClient _obd;
        private readonly UdsClient _uds;
        private readonly ConcurrentDictionary<string, LiveDataValue> _values = new();
        private readonly List<LiveDataPid> _activePids = new();
        private readonly object _lock = new();
        
        private CancellationTokenSource _cts;
        private Task _monitorTask;
        private int _updateIntervalMs = 100;
        private bool _disposed;

        public event Action<string, double?> OnValueUpdated;
        public event Action<Exception> OnError;

        public IReadOnlyDictionary<string, LiveDataValue> Values => _values;
        public bool IsRunning => _monitorTask != null && !_monitorTask.IsCompleted;

        public LiveDataMonitor(ObdIIClient obd, UdsClient uds = null)
        {
            _obd = obd;
            _uds = uds;
        }

        /// <summary>
        /// Standard PIDs for Hyundai i30 Gamma 1.4
        /// </summary>
        public static List<LiveDataPid> GetDefaultPids()
        {
            return new List<LiveDataPid>
            {
                new LiveDataPid
                {
                    Id = "RPM", Name = "Engine Speed", Unit = "rpm",
                    Pid = 0x0C, MinValue = 0, MaxValue = 8000, Format = "F0",
                    Parser = data => data?.Length >= 4 ? ((data[2] * 256) + data[3]) / 4.0 : null
                },
                new LiveDataPid
                {
                    Id = "VSS", Name = "Vehicle Speed", Unit = "km/h",
                    Pid = 0x0D, MinValue = 0, MaxValue = 255, Format = "F0",
                    Parser = data => data?.Length >= 3 ? (double?)data[2] : null
                },
                new LiveDataPid
                {
                    Id = "ECT", Name = "Coolant Temperature", Unit = "°C",
                    Pid = 0x05, MinValue = -40, MaxValue = 215, Format = "F0",
                    Parser = data => data?.Length >= 3 ? data[2] - 40.0 : null
                },
                new LiveDataPid
                {
                    Id = "IAT", Name = "Intake Air Temp", Unit = "°C",
                    Pid = 0x0F, MinValue = -40, MaxValue = 215, Format = "F0",
                    Parser = data => data?.Length >= 3 ? data[2] - 40.0 : null
                },
                new LiveDataPid
                {
                    Id = "LOAD", Name = "Engine Load", Unit = "%",
                    Pid = 0x04, MinValue = 0, MaxValue = 100, Format = "F1",
                    Parser = data => data?.Length >= 3 ? data[2] * 100.0 / 255.0 : null
                },
                new LiveDataPid
                {
                    Id = "TPS", Name = "Throttle Position", Unit = "%",
                    Pid = 0x11, MinValue = 0, MaxValue = 100, Format = "F1",
                    Parser = data => data?.Length >= 3 ? data[2] * 100.0 / 255.0 : null
                },
                new LiveDataPid
                {
                    Id = "MAF", Name = "Mass Air Flow", Unit = "g/s",
                    Pid = 0x10, MinValue = 0, MaxValue = 655, Format = "F2",
                    Parser = data => data?.Length >= 4 ? ((data[2] * 256) + data[3]) / 100.0 : null
                },
                new LiveDataPid
                {
                    Id = "MAP", Name = "Intake Manifold Pressure", Unit = "kPa",
                    Pid = 0x0B, MinValue = 0, MaxValue = 255, Format = "F0",
                    Parser = data => data?.Length >= 3 ? (double?)data[2] : null
                },
                new LiveDataPid
                {
                    Id = "SPARK", Name = "Timing Advance", Unit = "°",
                    Pid = 0x0E, MinValue = -64, MaxValue = 64, Format = "F1",
                    Parser = data => data?.Length >= 3 ? (data[2] / 2.0) - 64 : null
                },
                new LiveDataPid
                {
                    Id = "STFT1", Name = "Short Term Fuel Trim B1", Unit = "%",
                    Pid = 0x06, MinValue = -100, MaxValue = 99.2, Format = "F1",
                    Parser = data => data?.Length >= 3 ? (data[2] - 128) * 100.0 / 128.0 : null
                },
                new LiveDataPid
                {
                    Id = "LTFT1", Name = "Long Term Fuel Trim B1", Unit = "%",
                    Pid = 0x07, MinValue = -100, MaxValue = 99.2, Format = "F1",
                    Parser = data => data?.Length >= 3 ? (data[2] - 128) * 100.0 / 128.0 : null
                },
                new LiveDataPid
                {
                    Id = "O2B1S1", Name = "O2 Sensor B1S1", Unit = "V",
                    Pid = 0x14, MinValue = 0, MaxValue = 1.275, Format = "F3",
                    Parser = data => data?.Length >= 3 ? data[2] / 200.0 : null
                },
                new LiveDataPid
                {
                    Id = "O2B1S2", Name = "O2 Sensor B1S2", Unit = "V",
                    Pid = 0x16, MinValue = 0, MaxValue = 1.275, Format = "F3",
                    Parser = data => data?.Length >= 3 ? data[2] / 200.0 : null
                },
                new LiveDataPid
                {
                    Id = "BATT", Name = "Battery Voltage", Unit = "V",
                    Pid = 0x42, MinValue = 0, MaxValue = 65.5, Format = "F2",
                    Parser = data => data?.Length >= 4 ? ((data[2] * 256) + data[3]) / 1000.0 : null
                },
                new LiveDataPid
                {
                    Id = "FUEL_PRESS", Name = "Fuel Pressure", Unit = "kPa",
                    Pid = 0x0A, MinValue = 0, MaxValue = 765, Format = "F0",
                    Parser = data => data?.Length >= 3 ? data[2] * 3.0 : null
                },
                new LiveDataPid
                {
                    Id = "CAT_B1S1", Name = "Catalyst Temp B1S1", Unit = "°C",
                    Pid = 0x3C, MinValue = -40, MaxValue = 6513.5, Format = "F1",
                    Parser = data => data?.Length >= 4 ? (((data[2] * 256) + data[3]) / 10.0) - 40 : null
                },
                new LiveDataPid
                {
                    Id = "RUNTIME", Name = "Engine Runtime", Unit = "sec",
                    Pid = 0x1F, MinValue = 0, MaxValue = 65535, Format = "F0",
                    Parser = data => data?.Length >= 4 ? (data[2] * 256) + data[3] : null
                },
                new LiveDataPid
                {
                    Id = "AMBIENT", Name = "Ambient Air Temp", Unit = "°C",
                    Pid = 0x46, MinValue = -40, MaxValue = 215, Format = "F0",
                    Parser = data => data?.Length >= 3 ? data[2] - 40.0 : null
                }
            };
        }

        public void SetUpdateInterval(int milliseconds)
        {
            _updateIntervalMs = Math.Max(50, milliseconds);
        }

        public void AddPid(LiveDataPid pid)
        {
            lock (_lock)
            {
                if (!_activePids.Any(p => p.Id == pid.Id))
                {
                    _activePids.Add(pid);
                    _values[pid.Id] = new LiveDataValue { Pid = pid };
                }
            }
        }

        public void AddPids(IEnumerable<LiveDataPid> pids)
        {
            foreach (var pid in pids)
                AddPid(pid);
        }

        public void RemovePid(string id)
        {
            lock (_lock)
            {
                _activePids.RemoveAll(p => p.Id == id);
                _values.TryRemove(id, out _);
            }
        }

        public void ClearPids()
        {
            lock (_lock)
            {
                _activePids.Clear();
                _values.Clear();
            }
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
            
            Logger.Instance.Info(TAG, $"Started monitoring {_activePids.Count} PIDs");
        }

        public void Stop()
        {
            _cts?.Cancel();
            
            try { _monitorTask?.Wait(1000); } catch { }
            
            _cts?.Dispose();
            _cts = null;
            _monitorTask = null;
            
            Logger.Instance.Info(TAG, "Monitoring stopped");
        }

        private async Task MonitorLoop(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            int sampleCount = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    List<LiveDataPid> pidsToQuery;
                    lock (_lock)
                    {
                        pidsToQuery = _activePids.Where(p => p.IsSupported).ToList();
                    }

                    foreach (var pid in pidsToQuery)
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            byte[] response = null;

                            if (pid.Did.HasValue && _uds != null)
                            {
                                // UDS query
                                var udsResp = _uds.ReadDataByIdentifier(pid.Did.Value);
                                if (udsResp.IsPositive)
                                    response = udsResp.Data;
                            }
                            else
                            {
                                // OBD-II query
                                response = _obd.QueryMode01(pid.Pid);
                            }

                            double? value = pid.Parser?.Invoke(response);
                            
                            if (_values.TryGetValue(pid.Id, out var lv))
                            {
                                lv.Update(value);
                            }

                            OnValueUpdated?.Invoke(pid.Id, value);
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Debug(TAG, $"PID {pid.Id} error: {ex.Message}");
                            
                            if (_values.TryGetValue(pid.Id, out var lv))
                            {
                                lv.ErrorCount++;
                                if (lv.ErrorCount > 5)
                                {
                                    pid.IsSupported = false;
                                    Logger.Instance.Warn(TAG, $"PID {pid.Id} disabled due to errors");
                                }
                            }
                        }
                    }

                    sampleCount++;
                    
                    // Calculate actual update rate every 10 seconds
                    if (sw.ElapsedMilliseconds >= 10000)
                    {
                        double rate = sampleCount * 1000.0 / sw.ElapsedMilliseconds;
                        Logger.Instance.Debug(TAG, $"Sample rate: {rate:F1} Hz");
                        sampleCount = 0;
                        sw.Restart();
                    }

                    await Task.Delay(_updateIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                    await Task.Delay(500, ct);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            Stop();
        }
    }

    /// <summary>
    /// Tracked live data value with statistics
    /// </summary>
    public class LiveDataValue
    {
        public LiveDataPid Pid { get; set; }
        public double? CurrentValue { get; private set; }
        public double? MinValue { get; private set; }
        public double? MaxValue { get; private set; }
        public double? AverageValue { get; private set; }
        public DateTime LastUpdate { get; private set; }
        public int SampleCount { get; private set; }
        public int ErrorCount { get; set; }

        private double _sum;

        public void Update(double? value)
        {
            CurrentValue = value;
            LastUpdate = DateTime.Now;

            if (value.HasValue)
            {
                if (!MinValue.HasValue || value < MinValue)
                    MinValue = value;
                if (!MaxValue.HasValue || value > MaxValue)
                    MaxValue = value;

                _sum += value.Value;
                SampleCount++;
                AverageValue = _sum / SampleCount;
            }
        }

        public void Reset()
        {
            CurrentValue = null;
            MinValue = null;
            MaxValue = null;
            AverageValue = null;
            SampleCount = 0;
            _sum = 0;
            ErrorCount = 0;
        }

        public override string ToString()
        {
            if (!CurrentValue.HasValue)
                return $"{Pid?.Name}: --";

            string format = Pid?.Format ?? "F1";
            return $"{Pid?.Name}: {CurrentValue.Value.ToString(format)} {Pid?.Unit}";
        }
    }

    #endregion

    #region Service Functions & Active Tests

    /// <summary>
    /// Professional Service Functions (MaxiSys Style)
    /// </summary>
    public class ServiceFunctions
    {
        private readonly UdsClient _client;

        public ServiceFunctions(UdsClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Performs Steering Angle Sensor (SAS) Calibration
        /// Required after alignment or steering parts replacement.
        /// Target ECU: ABS/ESP or MDPS
        /// </summary>
        
        public async Task<bool> CalibrateSteeringAngleSensor()
        {
            Logger.Instance.Info("Service", "Starting SAS Calibration...");

            if (!_client.StartExtendedSession().IsPositive) return false;
            if (!_client.SecurityAccess().IsPositive) return false;

            var result = _client.StartRoutine(HyundaiRID.SAS_CALIBRATION);

            if (result.IsPositive)
            {
                await Task.Delay(2000);
                var routineResult = _client.RequestRoutineResults(HyundaiRID.SAS_CALIBRATION);
                
                if (routineResult.IsPositive)
                {
                    Logger.Instance.Info("Service", "SAS Calibration Successful");
                    return true;
                }
            }

            Logger.Instance.Error("Service", $"SAS Calibration Failed: {result.ErrorMessage}");
            return false;
        }

        /// <summary>
        /// Resets Adaptive Values (Fuel Trims, Transmission Logic)
        /// </summary>
        public bool ResetAdaptiveValues()
        {
            Logger.Instance.Info("Service", "Resetting Adaptive Values...");
            if (!_client.StartExtendedSession().IsPositive) return false;
            
            // Hyundai often uses a specific Routine or Data Write for this
            // 0x01 = Reset All Adaptations
            return _client.StartRoutine(0x2A00, new byte[] { 0x01 }).IsPositive;
        }

        /// <summary>
        /// ABS HCU Air Bleeding Mode
        /// </summary>
        public async Task<bool> PerformAbsBleed()
        {
            Logger.Instance.Info("Service", "Starting ABS HCU Bleed...");
            if (!_client.StartExtendedSession().IsPositive) return false;
            if (!_client.SecurityAccess().IsPositive) return false;

            // Start Pump Motor routine
            var result = _client.StartRoutine(HyundaiRID.ABS_BLEED);
            if (!result.IsPositive) return false;

            // Keep pump running for 10 seconds or until ECU stops it
            await Task.Delay(10000); 
            _client.StopRoutine(HyundaiRID.ABS_BLEED);
            
            return true;
        }
    }

    /// <summary>
    /// Bi-Directional Control (Active Tests)
    /// </summary>
    public class ActiveTesting
    {
        private readonly UdsClient _client;

        public ActiveTesting(UdsClient client)
        {
            _client = client;
        }

        // Common Hyundai i30 Actuator DIDs (Need verification with GDS for specific ROM ID)
        public const ushort DID_FAN_CONTROL = 0x0300;
        public const ushort DID_FUEL_PUMP = 0x0301;
        public const ushort DID_AC_COMPRESSOR = 0x0302;
        public const ushort DID_IGNITION_COIL = 0x0303;

        /// <summary>
        /// Controls the Radiator Fan (Low/High)
        /// </summary>
        public bool ControlRadiatorFan(bool on, bool highSpeed = false)
        {
            _client.StartExtendedSession();
            
            byte state = on ? (highSpeed ? (byte)0x02 : (byte)0x01) : (byte)0x00;
            // IO Control: 0x03 (Short Term Adjust), State, Mask(optional)
            return _client.IoControlShortTerm(DID_FAN_CONTROL, new byte[] { state }).IsPositive;
        }

        /// <summary>
        /// Disables individual injectors (Cylinder Power Balance)
        /// </summary>
        public bool CutInjector(int cylinderId, bool cut)
        {
            if (cylinderId < 1 || cylinderId > 4) return false;
            _client.StartExtendedSession();

            // Typically Hyundai uses a bitmask for injectors on a specific DID
            byte mask = (byte)(1 << (cylinderId - 1));
            byte state = cut ? (byte)0x00 : mask; 

            return _client.IoControlShortTerm(0x0350, new byte[] { state, mask }).IsPositive;
        }

        /// <summary>
        /// Returns control of an actuator to the ECU (Release)
        /// </summary>
        public void ReleaseControl(ushort did)
        {
            _client.IoControlReturnToEcu(did);
        }
    }

    #endregion

     #region Program Entry Point

    /// <summary>
    /// Main application entry point
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     HYUNDAI i30 (FD) 2012 1.4L DIAGNOSTIC SUITE v3.0        ║");
            Console.WriteLine("║     Target: G4FA Gamma 109PS - ISO 15765-4 CAN              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            J2534Api api = null;
            uint deviceId = 0;
            uint channelId = 0;

            try
            {
                var device = SelectJ2534Device();
                if (device == null)
                {
                    Console.WriteLine("[ERROR] No compatible J2534 device found or selection cancelled.");
                    return;
                }

                Console.WriteLine($"[OK] Using: {device.Name} ({device.Vendor})");

                api = new J2534Api(device.DllPath);

                var result = api.PassThruOpen(out deviceId);
                if (!J2534Error.IsSuccess(result))
                {
                    Console.WriteLine($"[ERROR] Failed to open device: {J2534Error.GetDescription(result)}");
                    return;
                }
                Console.WriteLine("[OK] Device opened");

                if (J2534Error.IsSuccess(api.ReadBatteryVoltage(deviceId, out double voltage)))
                {
                    Console.WriteLine($"[OK] Battery Voltage: {voltage:F2}V");
                }

                result = api.PassThruConnect(deviceId, J2534Const.ISO15765, 0,
                    VehicleConfig.CAN_BAUD_RATE, out channelId);
                if (!J2534Error.IsSuccess(result))
                {
                    Console.WriteLine($"[ERROR] Failed to connect: {J2534Error.GetDescription(result)}");
                    return;
                }
                Console.WriteLine("[OK] ISO15765 channel connected at 500kbps");

                api.SetConfiguration(channelId,
                    (J2534Const.ISO15765_BS, VehicleConfig.ISO_TP_BLOCK_SIZE),
                    (J2534Const.ISO15765_STMIN, VehicleConfig.ISO_TP_ST_MIN));

                result = api.SetupFlowControlFilter(channelId,
                    VehicleConfig.ECM_REQUEST_ID,
                    VehicleConfig.ECM_RESPONSE_ID,
                    out uint filterId);
                if (!J2534Error.IsSuccess(result))
                {
                    Console.WriteLine($"[ERROR] Filter setup failed: {J2534Error.GetDescription(result)}");
                    return;
                }
                Console.WriteLine("[OK] Flow control filter established");

                var obdClient = new ObdIIClient(api, channelId);

                Console.WriteLine("\n--- Vehicle Identification ---");
                string vin = obdClient.ReadVin();
                Console.WriteLine($"VIN: {vin ?? "Unable to read"}");

                var milStatus = obdClient.ReadMilStatus();
                if (milStatus.HasValue)
                {
                    Console.WriteLine($"MIL (Check Engine Light): {(milStatus.Value.mil ? "ON" : "OFF")}");
                    Console.WriteLine($"Stored DTC Count: {milStatus.Value.dtcCount}");
                }

                Console.WriteLine("\n--- Diagnostic Trouble Codes ---");
                var dtcs = obdClient.ReadCurrentDtcs();
                if (dtcs.Count > 0)
                {
                    foreach (var dtc in dtcs)
                    {
                        HyundaiDtcDatabase.TryGetDescription(dtc, out var desc);
                        Console.WriteLine($"  [{dtc}] {desc ?? "Unknown"}");
                    }
                }
                else
                {
                    Console.WriteLine("  No DTCs stored");
                }

                var ecmDef = VehicleConfig.EcuAddresses["ECM"];
                using var udsClient = new UdsClient(api, channelId, ecmDef);

                Console.WriteLine("\n--- ECU Information ---");
                var ecuInfo = udsClient.ReadEcuInfo();
                Console.WriteLine($"Part Number: {ecuInfo.partNumber ?? "N/A"}");
                Console.WriteLine($"Software Version: {ecuInfo.softwareVersion ?? "N/A"}");
                Console.WriteLine($"Hardware Version: {ecuInfo.hardwareVersion ?? "N/A"}");

                Console.WriteLine("\n--- Live Data (Press any key to stop) ---");

                using var liveMonitor = new LiveDataMonitor(obdClient, udsClient);
                liveMonitor.AddPids(LiveDataMonitor.GetDefaultPids().Take(8));
                liveMonitor.SetUpdateInterval(200);

                liveMonitor.OnValueUpdated += (id, value) =>
                {
                    if (liveMonitor.Values.TryGetValue(id, out var lv) && lv.CurrentValue.HasValue)
                    {
                        Console.Write($"\r{lv.Pid.Name,-25} {lv.CurrentValue.Value.ToString(lv.Pid.Format),10} {lv.Pid.Unit,-8}");
                    }
                };

                liveMonitor.Start();

                while (!Console.KeyAvailable)
                {
                    await Task.Delay(100);
                }
                Console.ReadKey(true);

                liveMonitor.Stop();
                Console.WriteLine("\n\n--- Session Complete ---");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[CRITICAL ERROR] {ex.Message}");
                Logger.Instance.Critical("Main", ex.Message, ex);
            }
            finally
            {
                if (channelId != 0)
                    api?.PassThruDisconnect(channelId);
                if (deviceId != 0)
                    api?.PassThruClose(deviceId);
                api?.Dispose();
                Logger.Instance.Dispose();

                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        private static J2534DeviceInfo SelectJ2534Device()
        {
            var devices = J2534DeviceScanner.GetCompatibleDevices();
            if (devices == null || devices.Count == 0)
            {
                Console.WriteLine("[ERROR] No compatible J2534 devices found.");
                return null;
            }

            Console.WriteLine("--- Available J2534 Devices ---");
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                Console.WriteLine($"[{i + 1}] {d.Name}  ({d.Vendor})  [{(d.Is32Bit ? "x86" : "x64")}]");
            }

            // Prefer KTS560 if present, otherwise first device
            int defaultIndex = 0;
            int kts560Index = devices.FindIndex(d =>
                d.Name.Contains("KTS560", StringComparison.OrdinalIgnoreCase));
            if (kts560Index >= 0)
                defaultIndex = kts560Index;

            Console.WriteLine();
            Console.Write($"Select device [1-{devices.Count}] (default {defaultIndex + 1}): ");

            while (true)
            {
                string input = Console.ReadLine();

                // Enter = default
                if (string.IsNullOrWhiteSpace(input))
                    return devices[defaultIndex];

                if (int.TryParse(input, out int choice) &&
                    choice >= 1 && choice <= devices.Count)
                {
                    return devices[choice - 1];
                }

                Console.Write($"Invalid selection. Enter a number 1-{devices.Count} or press Enter for default ({defaultIndex + 1}): ");
            }
        }
    }

    #endregion

} // End namespace HyundaiDiagnosticSuite
