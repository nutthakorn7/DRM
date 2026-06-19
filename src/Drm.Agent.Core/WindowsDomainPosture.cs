using System.Runtime.InteropServices;

namespace Drm.Agent.Core;

public static class WindowsDomainPosture
{
    private const int NetSetupDomainName = 3;
    private const uint NoActiveConsoleSession = 0xFFFFFFFF;

    public static AgentDevicePosture Capture()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return AgentDevicePosture.Unknown;
        }

        nint nameBuffer = nint.Zero;
        try
        {
            var status = NetGetJoinInformation(null, out nameBuffer, out var joinStatus);
            var domainName = nameBuffer == nint.Zero
                ? ""
                : Marshal.PtrToStringUni(nameBuffer) ?? "";
            var domainJoined = status == 0
                && joinStatus == NetSetupDomainName
                && !string.IsNullOrWhiteSpace(domainName);

            return new AgentDevicePosture(domainJoined, domainJoined ? domainName : "", CaptureWindowsUser());
        }
        catch
        {
            return AgentDevicePosture.Unknown;
        }
        finally
        {
            if (nameBuffer != nint.Zero)
            {
                _ = NetApiBufferFree(nameBuffer);
            }
        }
    }

    private static string CaptureWindowsUser()
    {
        try
        {
            var interactiveUser = CaptureActiveConsoleUser();
            if (!string.IsNullOrWhiteSpace(interactiveUser))
            {
                return interactiveUser;
            }

            var userName = Environment.UserName ?? "";
            if (string.IsNullOrWhiteSpace(userName))
            {
                return "";
            }

            var domainName = Environment.UserDomainName ?? "";
            return string.IsNullOrWhiteSpace(domainName) ? userName : $"{domainName}\\{userName}";
        }
        catch
        {
            return Environment.UserName ?? "";
        }
    }

    private static string CaptureActiveConsoleUser()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoActiveConsoleSession)
        {
            return "";
        }

        var userName = QuerySessionString(sessionId, WtsInfoClass.UserName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "";
        }

        var domainName = QuerySessionString(sessionId, WtsInfoClass.DomainName);
        return string.IsNullOrWhiteSpace(domainName) ? userName : $"{domainName}\\{userName}";
    }

    private static string QuerySessionString(uint sessionId, WtsInfoClass infoClass)
    {
        nint buffer = nint.Zero;
        try
        {
            if (!WTSQuerySessionInformation(nint.Zero, sessionId, infoClass, out buffer, out var bytesReturned) ||
                buffer == nint.Zero ||
                bytesReturned <= 1)
            {
                return "";
            }

            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            if (buffer != nint.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(
        string? server,
        out nint nameBuffer,
        out int bufferType);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(nint buffer);

    [DllImport("Kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        nint server,
        uint sessionId,
        WtsInfoClass wtsInfoClass,
        out nint buffer,
        out int bytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint buffer);

    private enum WtsInfoClass
    {
        UserName = 5,
        DomainName = 7
    }
}
