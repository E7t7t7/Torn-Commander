using System.Runtime.InteropServices;
using System.Text;

namespace TornCommander;

internal static class CredentialManager
{
    private const string LegacyTarget = "TornCommander:TornApiKey";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    private static string TargetFor(string profileId)
    {
        var safe = string.Concat((profileId ?? "main").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = "main";
        return $"TornCommander:{safe}:TornApiKey";
    }

    public static void SaveApiKey(string apiKey, string profileId = "main")
    {
        apiKey = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key cannot be empty.");
        Write(TargetFor(profileId), apiKey);
        if (profileId.Equals("main", StringComparison.OrdinalIgnoreCase))
            Write(LegacyTarget, apiKey); // compatibility with pre-profile builds
    }

    public static string? LoadApiKey(string profileId = "main")
    {
        var hit = Read(TargetFor(profileId));
        if (!string.IsNullOrWhiteSpace(hit)) return hit;
        return profileId.Equals("main", StringComparison.OrdinalIgnoreCase) ? Read(LegacyTarget) : null;
    }

    public static void ForgetApiKey(string profileId = "main")
    {
        CredDelete(TargetFor(profileId), CredTypeGeneric, 0);
        if (profileId.Equals("main", StringComparison.OrdinalIgnoreCase)) CredDelete(LegacyTarget, CredTypeGeneric, 0);
    }

    private static string IntelTarget(string profile,bool client) => "TornCommander:"+new string(profile.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_').ToArray())+(client?":TornIntelClientKey":":TornIntelLoginKey");
    public static void SaveIntelKey(string profile,string value,bool client){if(string.IsNullOrWhiteSpace(value)||value.Length>512||value.Any(char.IsControl))throw new ArgumentException("Invalid credential.");Write(IntelTarget(profile,client),value.Trim());}
    public static string? LoadIntelKey(string profile,bool client)=>Read(IntelTarget(profile,client));
    public static void ForgetIntelKey(string profile,bool client)=>CredDelete(IntelTarget(profile,client),CredTypeGeneric,0);

    private static string ScouterTarget(string profile)=>"TornCommander:"+new string(profile.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_').ToArray())+":FFScouterKey";
    public static string? LoadScouterKey(string profile)=>Read(ScouterTarget(profile));
    public static void SaveScouterKey(string profile,string key){key=key.Trim();if(key.Length!=16||!key.All(char.IsLetterOrDigit))throw new ArgumentException("FFScouter keys must be 16 letters/numbers.");Write(ScouterTarget(profile),key);}
    public static void ForgetScouterKey(string profile)=>CredDelete(ScouterTarget(profile),CredTypeGeneric,0);

    private static void Write(string target, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length + 2);
        try
        {
            for (int i = 0; i < bytes.Length + 2; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref cred, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            for (int i = 0; i < bytes.Length + 2; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static string? Read(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
        }
        finally { CredFree(ptr); }
    }
}
