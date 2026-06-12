# PrintBridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single Windows app (installed on every PC on a LAN) that lets each PC share its printers and print to other PCs' shared printers, with no printer driver needed on the printing PC.

**Architecture:** One C#/.NET Framework 4.8 WinForms tray app runs on every PC in a dual role. Sharing role: advertises ticked local printers over UDP and accepts print jobs over TCP, printing them via bundled Ghostscript. Using role: discovers remote shared printers, and installs a virtual printer (Microsoft PostScript driver + mfilemon redirection port) so any app's `File → Print` is captured as PostScript and shipped to the owning PC. A shared `PrintBridge.Protocol` library (pure logic) carries discovery + job framing and is fully unit-tested; Windows-spooler code is isolated behind interfaces and verified by integration tests on real Windows PCs.

**Tech Stack:** C#, .NET Framework 4.8, WinForms, Newtonsoft.Json, Ghostscript (bundled), mfilemon port monitor (self-signed under Windows test-signing), xUnit for tests, Visual Studio 2022 Build Tools.

**Reference spec:** `docs/superpowers/specs/2026-06-12-printbridge-design.md`

**IMPORTANT — build/test environment:** Authoring happens on macOS, but **every build, test, and verification command in this plan runs on a Windows PC** (Windows 10/11 with Visual Studio 2022 or Build Tools for VS + the .NET Framework 4.8 targeting pack + .NET SDK 8 for `dotnet test`). Do not attempt to compile or run these on macOS.

---

## File Structure

```
PrintBridge.sln
src/
  PrintBridge.Protocol/            (net48 class library — pure logic, no Windows APIs)
    PrintBridge.Protocol.csproj
    FrameCodec.cs                  length-prefixed frame read/write over a Stream
    DiscoveryBeacon.cs             UDP beacon model + JSON encode/decode
    JobHeader.cs                   per-job metadata model + JSON encode/decode
    JobResult.cs                   server response model + JSON encode/decode
    ServerEntry.cs                 a discovered remote PC + its shared printers
    ServerRegistry.cs              live table of discovered servers with TTL expiry
    Json.cs                        single Newtonsoft.Json settings + helpers
  PrintBridge.Spooler/             (net48 class library — Windows printer APIs)
    PrintBridge.Spooler.csproj
    IPrinterService.cs             interface: enumerate, print-ps, install/remove virtual printer
    GhostscriptCommand.cs          builds gs argument string (pure → unit-tested)
    GhostscriptPrinter.cs          runs Ghostscript to print a .ps to a named printer
    PrinterEnumerator.cs           lists locally installed printers
    VirtualPrinterInstaller.cs     installs/removes the PostScript virtual printer + mfilemon port
  PrintBridge.App/                 (net48 WinForms exe — the one app on every PC)
    PrintBridge.App.csproj
    Program.cs                     entry point; routes --capture vs GUI; single-instance
    AppConfig.cs                   load/save JSON config (shared printers, PIN, active mapping)
    CaptureMode.cs                 --capture: read PostScript from stdin → named pipe → running app
    SharingService.cs             UDP beacon broadcaster + TCP job listener
    DiscoveryService.cs           UDP beacon listener feeding a ServerRegistry
    JobSender.cs                   TCP client that sends a captured job to a server
    MainForm.cs                    two-tab GUI (My Printers / Network Printers) + tray icon
tests/
  PrintBridge.Protocol.Tests/      (net48 xUnit)
    PrintBridge.Protocol.Tests.csproj
    FrameCodecTests.cs
    DiscoveryBeaconTests.cs
    JobHeaderTests.cs
    JobResultTests.cs
    ServerRegistryTests.cs
  PrintBridge.Spooler.Tests/       (net48 xUnit)
    PrintBridge.Spooler.Tests.csproj
    GhostscriptCommandTests.cs
installer/
  PrintBridge.iss                  Inno Setup script (bundles app + Ghostscript + mfilemon)
  install-virtual-printer.ps1      sets up port monitor + virtual printer + test cert
  uninstall-virtual-printer.ps1    tears it all down
third_party/
  ghostscript/                     bundled gs binaries (added during Phase 5)
  mfilemon/                        bundled mfilemon DLLs (added during Phase 4/5)
```

---

## Phase 0 — Solution scaffold

### Task 0.1: Create the solution and Protocol library project

**Files:**
- Create: `PrintBridge.sln`
- Create: `src/PrintBridge.Protocol/PrintBridge.Protocol.csproj`

- [ ] **Step 1: Create solution + project (run on Windows)**

```bat
dotnet new sln -n PrintBridge
dotnet new classlib -n PrintBridge.Protocol -o src/PrintBridge.Protocol -f net48
dotnet sln add src/PrintBridge.Protocol/PrintBridge.Protocol.csproj
del src\PrintBridge.Protocol\Class1.cs
dotnet add src/PrintBridge.Protocol package Newtonsoft.Json --version 13.0.3
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/PrintBridge.Protocol/PrintBridge.Protocol.csproj`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Commit**

```bat
git add PrintBridge.sln src/PrintBridge.Protocol
git commit -m "chore: scaffold solution and Protocol library"
```

### Task 0.2: Create the Protocol test project

**Files:**
- Create: `tests/PrintBridge.Protocol.Tests/PrintBridge.Protocol.Tests.csproj`

- [ ] **Step 1: Create xUnit test project**

```bat
dotnet new xunit -n PrintBridge.Protocol.Tests -o tests/PrintBridge.Protocol.Tests -f net48
dotnet sln add tests/PrintBridge.Protocol.Tests/PrintBridge.Protocol.Tests.csproj
dotnet add tests/PrintBridge.Protocol.Tests reference src/PrintBridge.Protocol/PrintBridge.Protocol.csproj
del tests\PrintBridge.Protocol.Tests\UnitTest1.cs
```

- [ ] **Step 2: Verify the empty test project runs**

Run: `dotnet test tests/PrintBridge.Protocol.Tests/PrintBridge.Protocol.Tests.csproj`
Expected: build succeeds, `Passed! - Failed: 0, Passed: 0`.

- [ ] **Step 3: Commit**

```bat
git add tests/PrintBridge.Protocol.Tests
git commit -m "chore: scaffold Protocol test project"
```

---

## Phase 1 — Protocol library (pure logic, full TDD)

This phase has no Windows dependencies. Every task is red→green→commit.

### Task 1.1: JSON settings helper

**Files:**
- Create: `src/PrintBridge.Protocol/Json.cs`

- [ ] **Step 1: Write the implementation (no test — it's a config object exercised by later tests)**

```csharp
using Newtonsoft.Json;

namespace PrintBridge.Protocol
{
    public static class Json
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Serialize(object value) =>
            JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/PrintBridge.Protocol/PrintBridge.Protocol.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bat
git add src/PrintBridge.Protocol/Json.cs
git commit -m "feat: add JSON serialization helper"
```

### Task 1.2: FrameCodec — length-prefixed framing

**Files:**
- Create: `src/PrintBridge.Protocol/FrameCodec.cs`
- Test: `tests/PrintBridge.Protocol.Tests/FrameCodecTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using System.Text;
using PrintBridge.Protocol;
using Xunit;

public class FrameCodecTests
{
    [Fact]
    public void WriteThenRead_ReturnsSamePayload()
    {
        var payload = Encoding.UTF8.GetBytes("hello frame");
        using var ms = new MemoryStream();
        FrameCodec.WriteFrame(ms, payload);
        ms.Position = 0;
        var read = FrameCodec.ReadFrame(ms);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void WriteThenRead_TwoFrames_AreReadInOrder()
    {
        var a = Encoding.UTF8.GetBytes("first");
        var b = Encoding.UTF8.GetBytes("second");
        using var ms = new MemoryStream();
        FrameCodec.WriteFrame(ms, a);
        FrameCodec.WriteFrame(ms, b);
        ms.Position = 0;
        Assert.Equal(a, FrameCodec.ReadFrame(ms));
        Assert.Equal(b, FrameCodec.ReadFrame(ms));
    }

    [Fact]
    public void ReadFrame_OnEmptyStream_ThrowsEndOfStream()
    {
        using var ms = new MemoryStream();
        Assert.Throws<EndOfStreamException>(() => FrameCodec.ReadFrame(ms));
    }

    [Fact]
    public void WriteFrame_RejectsOversizedPayload()
    {
        using var ms = new MemoryStream();
        var tooBig = FrameCodec.MaxFrameBytes + 1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FrameCodec.WriteFrame(ms, new byte[tooBig]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter FrameCodecTests`
Expected: FAIL — `FrameCodec` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.IO;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// Length-prefixed framing: a 4-byte big-endian unsigned length, then that
    /// many payload bytes. Used for both the JSON header and the PostScript body.
    /// </summary>
    public static class FrameCodec
    {
        // 200 MB cap — generous for print spool data, guards against bad input.
        public const int MaxFrameBytes = 200 * 1024 * 1024;

        public static void WriteFrame(Stream stream, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxFrameBytes)
                throw new ArgumentOutOfRangeException(nameof(payload),
                    $"Frame of {payload.Length} bytes exceeds max {MaxFrameBytes}.");

            var len = payload.Length;
            var header = new byte[4];
            header[0] = (byte)((len >> 24) & 0xFF);
            header[1] = (byte)((len >> 16) & 0xFF);
            header[2] = (byte)((len >> 8) & 0xFF);
            header[3] = (byte)(len & 0xFF);
            stream.Write(header, 0, 4);
            stream.Write(payload, 0, payload.Length);
        }

        public static byte[] ReadFrame(Stream stream)
        {
            var header = ReadExactly(stream, 4);
            int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (len < 0 || len > MaxFrameBytes)
                throw new InvalidDataException($"Declared frame length {len} is invalid.");
            return ReadExactly(stream, len);
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Stream ended after {offset} of {count} bytes.");
                offset += read;
            }
            return buffer;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter FrameCodecTests`
Expected: PASS — 4 passed.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.Protocol/FrameCodec.cs tests/PrintBridge.Protocol.Tests/FrameCodecTests.cs
git commit -m "feat: add length-prefixed frame codec"
```

### Task 1.3: DiscoveryBeacon model + JSON round-trip

**Files:**
- Create: `src/PrintBridge.Protocol/DiscoveryBeacon.cs`
- Test: `tests/PrintBridge.Protocol.Tests/DiscoveryBeaconTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using PrintBridge.Protocol;
using Xunit;

public class DiscoveryBeaconTests
{
    [Fact]
    public void ToJson_FromJson_RoundTrips()
    {
        var beacon = new DiscoveryBeacon
        {
            AppVersion = "1.0.0",
            PcName = "OFFICE-PC",
            PcId = "11111111-1111-1111-1111-111111111111",
            JobPort = 49153,
            SharedPrinters = new List<string> { "HP LaserJet", "Canon MX" }
        };

        var json = beacon.ToJson();
        var back = DiscoveryBeacon.FromJson(json);

        Assert.Equal(beacon.PcName, back.PcName);
        Assert.Equal(beacon.PcId, back.PcId);
        Assert.Equal(beacon.JobPort, back.JobPort);
        Assert.Equal(beacon.SharedPrinters, back.SharedPrinters);
    }

    [Fact]
    public void FromJson_OnGarbage_ReturnsNull()
    {
        Assert.Null(DiscoveryBeacon.FromJson("not json at all"));
    }

    [Fact]
    public void FromJson_OnWrongMagic_ReturnsNull()
    {
        Assert.Null(DiscoveryBeacon.FromJson("{\"magic\":\"OTHER\"}"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter DiscoveryBeaconTests`
Expected: FAIL — `DiscoveryBeacon` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// Broadcast by every PC that has at least one shared printer, so others can
    /// discover it without manual IP entry. "Magic" guards against unrelated UDP
    /// traffic on the same port.
    /// </summary>
    public class DiscoveryBeacon
    {
        public const string ExpectedMagic = "PRINTBRIDGE/1";

        [JsonProperty("magic")]
        public string Magic { get; set; } = ExpectedMagic;

        [JsonProperty("appVersion")]
        public string AppVersion { get; set; }

        [JsonProperty("pcName")]
        public string PcName { get; set; }

        [JsonProperty("pcId")]
        public string PcId { get; set; }

        [JsonProperty("jobPort")]
        public int JobPort { get; set; }

        [JsonProperty("sharedPrinters")]
        public List<string> SharedPrinters { get; set; } = new List<string>();

        public string ToJson() => Json.Serialize(this);

        public static DiscoveryBeacon FromJson(string json)
        {
            try
            {
                var beacon = Json.Deserialize<DiscoveryBeacon>(json);
                if (beacon == null || beacon.Magic != ExpectedMagic) return null;
                return beacon;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter DiscoveryBeaconTests`
Expected: PASS — 3 passed.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.Protocol/DiscoveryBeacon.cs tests/PrintBridge.Protocol.Tests/DiscoveryBeaconTests.cs
git commit -m "feat: add discovery beacon model"
```

### Task 1.4: JobHeader model

**Files:**
- Create: `src/PrintBridge.Protocol/JobHeader.cs`
- Test: `tests/PrintBridge.Protocol.Tests/JobHeaderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using PrintBridge.Protocol;
using Xunit;

public class JobHeaderTests
{
    [Fact]
    public void ToJson_FromJson_RoundTrips()
    {
        var header = new JobHeader
        {
            JobId = "job-123",
            TargetPrinter = "HP LaserJet",
            Copies = 2,
            PaperSize = "A4",
            RequestingUser = "jassar",
            RequestingPc = "LAPTOP-1",
            Pin = "4242"
        };

        var back = JobHeader.FromJson(header.ToJson());

        Assert.Equal("job-123", back.JobId);
        Assert.Equal("HP LaserJet", back.TargetPrinter);
        Assert.Equal(2, back.Copies);
        Assert.Equal("A4", back.PaperSize);
        Assert.Equal("jassar", back.RequestingUser);
        Assert.Equal("LAPTOP-1", back.RequestingPc);
        Assert.Equal("4242", back.Pin);
    }

    [Fact]
    public void Copies_DefaultsToOne()
    {
        var header = new JobHeader();
        Assert.Equal(1, header.Copies);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter JobHeaderTests`
Expected: FAIL — `JobHeader` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Newtonsoft.Json;

namespace PrintBridge.Protocol
{
    /// <summary>Metadata sent ahead of the PostScript payload for a print job.</summary>
    public class JobHeader
    {
        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("targetPrinter")]
        public string TargetPrinter { get; set; }

        [JsonProperty("copies")]
        public int Copies { get; set; } = 1;

        [JsonProperty("paperSize")]
        public string PaperSize { get; set; }

        [JsonProperty("requestingUser")]
        public string RequestingUser { get; set; }

        [JsonProperty("requestingPc")]
        public string RequestingPc { get; set; }

        [JsonProperty("pin")]
        public string Pin { get; set; }

        public string ToJson() => Json.Serialize(this);
        public static JobHeader FromJson(string json) => Json.Deserialize<JobHeader>(json);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter JobHeaderTests`
Expected: PASS — 2 passed.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.Protocol/JobHeader.cs tests/PrintBridge.Protocol.Tests/JobHeaderTests.cs
git commit -m "feat: add job header model"
```

### Task 1.5: JobResult model

**Files:**
- Create: `src/PrintBridge.Protocol/JobResult.cs`
- Test: `tests/PrintBridge.Protocol.Tests/JobResultTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using PrintBridge.Protocol;
using Xunit;

public class JobResultTests
{
    [Fact]
    public void RoundTrips_WithStatusAndDetail()
    {
        var result = JobResult.Rejected("authentication failed");
        var back = JobResult.FromJson(result.ToJson());
        Assert.Equal(JobStatus.Rejected, back.Status);
        Assert.Equal("authentication failed", back.Detail);
    }

    [Fact]
    public void Printed_HasPrintedStatus()
    {
        Assert.Equal(JobStatus.Printed, JobResult.Printed().Status);
    }

    [Fact]
    public void Accepted_HasAcceptedStatus()
    {
        Assert.Equal(JobStatus.Accepted, JobResult.Accepted().Status);
    }

    [Fact]
    public void Error_CarriesDetail()
    {
        var r = JobResult.Error("could not render document");
        Assert.Equal(JobStatus.Error, r.Status);
        Assert.Equal("could not render document", r.Detail);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter JobResultTests`
Expected: FAIL — `JobResult`/`JobStatus` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PrintBridge.Protocol
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum JobStatus { Accepted, Rejected, Printed, Error }

    /// <summary>Server's response to a job: first Accepted/Rejected, then Printed/Error.</summary>
    public class JobResult
    {
        [JsonProperty("status")]
        public JobStatus Status { get; set; }

        [JsonProperty("detail")]
        public string Detail { get; set; }

        public static JobResult Accepted() => new JobResult { Status = JobStatus.Accepted };
        public static JobResult Rejected(string detail) =>
            new JobResult { Status = JobStatus.Rejected, Detail = detail };
        public static JobResult Printed() => new JobResult { Status = JobStatus.Printed };
        public static JobResult Error(string detail) =>
            new JobResult { Status = JobStatus.Error, Detail = detail };

        public string ToJson() => Json.Serialize(this);
        public static JobResult FromJson(string json) => Json.Deserialize<JobResult>(json);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter JobResultTests`
Expected: PASS — 4 passed.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.Protocol/JobResult.cs tests/PrintBridge.Protocol.Tests/JobResultTests.cs
git commit -m "feat: add job result model"
```

### Task 1.6: ServerEntry + ServerRegistry with TTL expiry

**Files:**
- Create: `src/PrintBridge.Protocol/ServerEntry.cs`
- Create: `src/PrintBridge.Protocol/ServerRegistry.cs`
- Test: `tests/PrintBridge.Protocol.Tests/ServerRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PrintBridge.Protocol;
using Xunit;

public class ServerRegistryTests
{
    private static DiscoveryBeacon Beacon(string pcId, string pcName, params string[] printers)
        => new DiscoveryBeacon
        {
            PcId = pcId, PcName = pcName, JobPort = 49153,
            SharedPrinters = printers.ToList(), AppVersion = "1.0.0"
        };

    [Fact]
    public void Observe_AddsServer()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var now = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", now);
        var active = reg.GetActive(now);
        Assert.Single(active);
        Assert.Equal("OFFICE", active[0].PcName);
        Assert.Equal("192.168.1.10", active[0].IpAddress);
        Assert.Equal(new List<string> { "HP" }, active[0].SharedPrinters);
    }

    [Fact]
    public void Observe_SamePcId_UpdatesNotDuplicates()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var now = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", now);
        reg.Observe(Beacon("pc1", "OFFICE", "HP", "Canon"), "192.168.1.10", now.AddSeconds(2));
        var active = reg.GetActive(now.AddSeconds(2));
        Assert.Single(active);
        Assert.Equal(2, active[0].SharedPrinters.Count);
    }

    [Fact]
    public void GetActive_ExcludesExpiredServers()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var t0 = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", t0);
        var later = t0.AddSeconds(11);
        Assert.Empty(reg.GetActive(later));
    }

    [Fact]
    public void GetActive_KeepsServerRefreshedWithinTtl()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var t0 = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", t0);
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", t0.AddSeconds(8));
        Assert.Single(reg.GetActive(t0.AddSeconds(15)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter ServerRegistryTests`
Expected: FAIL — `ServerRegistry`/`ServerEntry` do not exist.

- [ ] **Step 3: Write minimal implementation**

`ServerEntry.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PrintBridge.Protocol
{
    public class ServerEntry
    {
        public string PcId { get; set; }
        public string PcName { get; set; }
        public string IpAddress { get; set; }
        public int JobPort { get; set; }
        public List<string> SharedPrinters { get; set; } = new List<string>();
        public DateTime LastSeenUtc { get; set; }
    }
}
```

`ServerRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// Thread-safe live table of discovered servers. Entries expire when not
    /// refreshed within the TTL, so PCs that go offline drop out of the list.
    /// </summary>
    public class ServerRegistry
    {
        private readonly TimeSpan _ttl;
        private readonly Dictionary<string, ServerEntry> _byPcId =
            new Dictionary<string, ServerEntry>();
        private readonly object _lock = new object();

        public ServerRegistry(TimeSpan ttl) => _ttl = ttl;

        public void Observe(DiscoveryBeacon beacon, string ipAddress, DateTime nowUtc)
        {
            if (beacon == null || string.IsNullOrEmpty(beacon.PcId)) return;
            lock (_lock)
            {
                _byPcId[beacon.PcId] = new ServerEntry
                {
                    PcId = beacon.PcId,
                    PcName = beacon.PcName,
                    IpAddress = ipAddress,
                    JobPort = beacon.JobPort,
                    SharedPrinters = beacon.SharedPrinters ?? new List<string>(),
                    LastSeenUtc = nowUtc
                };
            }
        }

        public List<ServerEntry> GetActive(DateTime nowUtc)
        {
            lock (_lock)
            {
                return _byPcId.Values
                    .Where(e => nowUtc - e.LastSeenUtc <= _ttl)
                    .OrderBy(e => e.PcName)
                    .ToList();
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.Protocol.Tests --filter ServerRegistryTests`
Expected: PASS — 4 passed.

- [ ] **Step 5: Run the full Protocol suite and commit**

Run: `dotnet test tests/PrintBridge.Protocol.Tests`
Expected: PASS — all Phase-1 tests green.

```bat
git add src/PrintBridge.Protocol/ServerEntry.cs src/PrintBridge.Protocol/ServerRegistry.cs tests/PrintBridge.Protocol.Tests/ServerRegistryTests.cs
git commit -m "feat: add server registry with TTL expiry"
```

---

## Phase 2 — Spooler library

The pure command-builder is unit-tested; the API-touching methods are exercised by an integration check on a real Windows PC (with "Microsoft Print to PDF" as a safe target).

### Task 2.1: Scaffold Spooler library + test project

**Files:**
- Create: `src/PrintBridge.Spooler/PrintBridge.Spooler.csproj`
- Create: `tests/PrintBridge.Spooler.Tests/PrintBridge.Spooler.Tests.csproj`

- [ ] **Step 1: Create projects**

```bat
dotnet new classlib -n PrintBridge.Spooler -o src/PrintBridge.Spooler -f net48
dotnet sln add src/PrintBridge.Spooler/PrintBridge.Spooler.csproj
del src\PrintBridge.Spooler\Class1.cs
dotnet new xunit -n PrintBridge.Spooler.Tests -o tests/PrintBridge.Spooler.Tests -f net48
dotnet sln add tests/PrintBridge.Spooler.Tests/PrintBridge.Spooler.Tests.csproj
dotnet add tests/PrintBridge.Spooler.Tests reference src/PrintBridge.Spooler/PrintBridge.Spooler.csproj
del tests\PrintBridge.Spooler.Tests\UnitTest1.cs
```

- [ ] **Step 2: Add System.Drawing reference (for printer enumeration)**

Edit `src/PrintBridge.Spooler/PrintBridge.Spooler.csproj`, add inside an `<ItemGroup>`:

```xml
<Reference Include="System.Drawing" />
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/PrintBridge.Spooler/PrintBridge.Spooler.csproj`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bat
git add src/PrintBridge.Spooler tests/PrintBridge.Spooler.Tests
git commit -m "chore: scaffold Spooler library and tests"
```

### Task 2.2: GhostscriptCommand argument builder (pure → unit-tested)

**Files:**
- Create: `src/PrintBridge.Spooler/GhostscriptCommand.cs`
- Test: `tests/PrintBridge.Spooler.Tests/GhostscriptCommandTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using PrintBridge.Spooler;
using Xunit;

public class GhostscriptCommandTests
{
    [Fact]
    public void BuildArguments_TargetsNamedPrinterViaMswinpr2()
    {
        var args = GhostscriptCommand.BuildArguments(
            printerName: "HP LaserJet", postScriptPath: @"C:\temp\job.ps", copies: 1);

        Assert.Contains("-sDEVICE=mswinpr2", args);
        Assert.Contains("-sOutputFile=\"%printer%HP LaserJet\"", args);
        Assert.Contains("\"C:\\temp\\job.ps\"", args);
        Assert.Contains("-dBATCH", args);
        Assert.Contains("-dNOPAUSE", args);
    }

    [Fact]
    public void BuildArguments_IncludesCopiesWhenGreaterThanOne()
    {
        var args = GhostscriptCommand.BuildArguments("HP", @"C:\j.ps", copies: 3);
        Assert.Contains("-dNumCopies=3", args);
    }

    [Fact]
    public void BuildArguments_OmitsCopiesWhenOne()
    {
        var args = GhostscriptCommand.BuildArguments("HP", @"C:\j.ps", copies: 1);
        Assert.DoesNotContain("-dNumCopies", args);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.Spooler.Tests --filter GhostscriptCommandTests`
Expected: FAIL — `GhostscriptCommand` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text;

namespace PrintBridge.Spooler
{
    /// <summary>
    /// Builds the Ghostscript argument string that prints a PostScript file to a
    /// named Windows printer using the mswinpr2 device. This is the CutePDF/RedMon
    /// chain run in reverse (PostScript -> physical printer).
    /// </summary>
    public static class GhostscriptCommand
    {
        public static string BuildArguments(string printerName, string postScriptPath, int copies)
        {
            var sb = new StringBuilder();
            sb.Append("-dPrinted -dBATCH -dNOPAUSE -dNOSAFER -q ");
            if (copies > 1) sb.Append($"-dNumCopies={copies} ");
            sb.Append("-sDEVICE=mswinpr2 ");
            sb.Append($"-sOutputFile=\"%printer%{printerName}\" ");
            sb.Append($"\"{postScriptPath}\"");
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.Spooler.Tests --filter GhostscriptCommandTests`
Expected: PASS — 3 passed.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.Spooler/GhostscriptCommand.cs tests/PrintBridge.Spooler.Tests/GhostscriptCommandTests.cs
git commit -m "feat: add Ghostscript command builder"
```

### Task 2.3: IPrinterService interface + PrinterEnumerator

**Files:**
- Create: `src/PrintBridge.Spooler/IPrinterService.cs`
- Create: `src/PrintBridge.Spooler/PrinterEnumerator.cs`

- [ ] **Step 1: Write the interface**

```csharp
using System.Collections.Generic;

namespace PrintBridge.Spooler
{
    /// <summary>Abstraction over Windows printing so the App can be tested with fakes.</summary>
    public interface IPrinterService
    {
        /// <summary>Names of printers installed on this PC.</summary>
        IReadOnlyList<string> ListLocalPrinters();

        /// <summary>Print a PostScript file to a named local printer. Throws on failure.</summary>
        void PrintPostScript(string printerName, string postScriptPath, int copies);
    }
}
```

- [ ] **Step 2: Write the enumerator (uses System.Drawing.Printing)**

```csharp
using System.Collections.Generic;
using System.Drawing.Printing;

namespace PrintBridge.Spooler
{
    public class PrinterEnumerator
    {
        public IReadOnlyList<string> ListLocalPrinters()
        {
            var names = new List<string>();
            foreach (string name in PrinterSettings.InstalledPrinters)
                names.Add(name);
            return names;
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/PrintBridge.Spooler/PrintBridge.Spooler.csproj`
Expected: `Build succeeded`.

- [ ] **Step 4: Manual integration check (real Windows PC)**

Create a temporary console harness or use the C# Interactive window to call
`new PrinterEnumerator().ListLocalPrinters()` and confirm it lists at least
"Microsoft Print to PDF". Record the result in the commit message.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.Spooler/IPrinterService.cs src/PrintBridge.Spooler/PrinterEnumerator.cs
git commit -m "feat: add printer service interface and enumerator"
```

### Task 2.4: GhostscriptPrinter (runs gs) + IPrinterService implementation

**Files:**
- Create: `src/PrintBridge.Spooler/GhostscriptPrinter.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PrintBridge.Spooler
{
    /// <summary>
    /// Concrete IPrinterService. Enumerates printers and prints PostScript via the
    /// bundled Ghostscript console binary. The path to gs is injected so tests and
    /// installers can point at the bundled copy.
    /// </summary>
    public class GhostscriptPrinter : IPrinterService
    {
        private readonly string _ghostscriptExePath;
        private readonly PrinterEnumerator _enumerator = new PrinterEnumerator();

        public GhostscriptPrinter(string ghostscriptExePath)
        {
            if (!File.Exists(ghostscriptExePath))
                throw new FileNotFoundException("Ghostscript not found.", ghostscriptExePath);
            _ghostscriptExePath = ghostscriptExePath;
        }

        public IReadOnlyList<string> ListLocalPrinters() => _enumerator.ListLocalPrinters();

        public void PrintPostScript(string printerName, string postScriptPath, int copies)
        {
            if (!File.Exists(postScriptPath))
                throw new FileNotFoundException("PostScript file missing.", postScriptPath);

            var args = GhostscriptCommand.BuildArguments(printerName, postScriptPath, copies);
            var psi = new ProcessStartInfo
            {
                FileName = _ghostscriptExePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(120_000);
                if (!proc.HasExited)
                {
                    try { proc.Kill(); } catch { /* best effort */ }
                    throw new TimeoutException("Ghostscript timed out printing the job.");
                }
                if (proc.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Ghostscript failed (exit {proc.ExitCode}): {stderr}");
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/PrintBridge.Spooler/PrintBridge.Spooler.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bat
git add src/PrintBridge.Spooler/GhostscriptPrinter.cs
git commit -m "feat: add Ghostscript-backed printer service"
```

> Real printing is verified end-to-end in Phase 6 once Ghostscript is bundled
> (Phase 5) and a job can be produced (Phase 4).

---

## Phase 3 — App: config, networking services, GUI skeleton

### Task 3.1: Scaffold the WinForms app project

**Files:**
- Create: `src/PrintBridge.App/PrintBridge.App.csproj`

- [ ] **Step 1: Create WinForms exe targeting net48**

```bat
dotnet new winforms -n PrintBridge.App -o src/PrintBridge.App -f net48
dotnet sln add src/PrintBridge.App/PrintBridge.App.csproj
dotnet add src/PrintBridge.App reference src/PrintBridge.Protocol/PrintBridge.Protocol.csproj
dotnet add src/PrintBridge.App reference src/PrintBridge.Spooler/PrintBridge.Spooler.csproj
dotnet add src/PrintBridge.App package Newtonsoft.Json --version 13.0.3
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PrintBridge.App/PrintBridge.App.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bat
git add src/PrintBridge.App
git commit -m "chore: scaffold WinForms app"
```

### Task 3.2: AppConfig load/save

**Files:**
- Create: `src/PrintBridge.App/AppConfig.cs`
- Test: add `tests/PrintBridge.App.Tests` (new xUnit project) `AppConfigTests.cs`

- [ ] **Step 1: Create the App test project**

```bat
dotnet new xunit -n PrintBridge.App.Tests -o tests/PrintBridge.App.Tests -f net48
dotnet sln add tests/PrintBridge.App.Tests/PrintBridge.App.Tests.csproj
dotnet add tests/PrintBridge.App.Tests reference src/PrintBridge.App/PrintBridge.App.csproj
del tests\PrintBridge.App.Tests\UnitTest1.cs
```

- [ ] **Step 2: Write the failing test**

```csharp
using System.IO;
using PrintBridge.App;
using Xunit;

public class AppConfigTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var cfg = new AppConfig
            {
                SharedPrinters = { "HP LaserJet" },
                Pin = "4242",
                ActiveRemotePcId = "pc1",
                ActiveRemotePrinter = "Canon MX"
            };
            cfg.Save(path);
            var loaded = AppConfig.Load(path);
            Assert.Contains("HP LaserJet", loaded.SharedPrinters);
            Assert.Equal("4242", loaded.Pin);
            Assert.Equal("pc1", loaded.ActiveRemotePcId);
            Assert.Equal("Canon MX", loaded.ActiveRemotePrinter);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_OnMissingFile_ReturnsDefaults()
    {
        var loaded = AppConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.json"));
        Assert.Empty(loaded.SharedPrinters);
        Assert.Null(loaded.Pin);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/PrintBridge.App.Tests --filter AppConfigTests`
Expected: FAIL — `AppConfig` does not exist.

- [ ] **Step 4: Write minimal implementation**

```csharp
using System.Collections.Generic;
using System.IO;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    public class AppConfig
    {
        public List<string> SharedPrinters { get; set; } = new List<string>();
        public string Pin { get; set; }
        public string ActiveRemotePcId { get; set; }
        public string ActiveRemotePrinter { get; set; }

        public void Save(string path) => File.WriteAllText(path, Json.Serialize(this));

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return new AppConfig();
            var cfg = Json.Deserialize<AppConfig>(File.ReadAllText(path));
            return cfg ?? new AppConfig();
        }

        public static string DefaultPath =>
            Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PrintBridge", "config.json");
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/PrintBridge.App.Tests --filter AppConfigTests`
Expected: PASS — 2 passed.

- [ ] **Step 6: Commit**

```bat
git add src/PrintBridge.App/AppConfig.cs tests/PrintBridge.App.Tests
git commit -m "feat: add app config load/save"
```

### Task 3.3: SharingService — UDP beacon + TCP job listener

**Files:**
- Create: `src/PrintBridge.App/SharingService.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PrintBridge.Protocol;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    /// <summary>
    /// The "sharing" half of the app: broadcasts a discovery beacon listing shared
    /// printers, and accepts incoming print jobs, printing them via IPrinterService.
    /// </summary>
    public class SharingService : IDisposable
    {
        public const int DiscoveryPort = 49152;
        public const int DefaultJobPort = 49153;

        private readonly IPrinterService _printers;
        private readonly Func<AppConfig> _config;
        private readonly Func<IReadOnlyList<string>> _sharedPrinters;
        private readonly int _jobPort;
        private UdpClient _beaconSocket;
        private TcpListener _jobListener;
        private Timer _beaconTimer;
        private CancellationTokenSource _cts;
        private readonly string _pcId;
        private readonly string _pcName;

        public event Action<string> JobLogged;   // human-readable log lines for the GUI

        public SharingService(IPrinterService printers, Func<AppConfig> config,
            Func<IReadOnlyList<string>> sharedPrinters, string pcId, string pcName,
            int jobPort = DefaultJobPort)
        {
            _printers = printers;
            _config = config;
            _sharedPrinters = sharedPrinters;
            _pcId = pcId;
            _pcName = pcName;
            _jobPort = jobPort;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _beaconSocket = new UdpClient { EnableBroadcast = true };
            _beaconTimer = new Timer(_ => SendBeacon(), null, 0, 3000);

            _jobListener = new TcpListener(IPAddress.Any, _jobPort);
            _jobListener.Start();
            AcceptLoop();
        }

        private void SendBeacon()
        {
            try
            {
                var shared = _sharedPrinters();
                if (shared == null || shared.Count == 0) return; // only advertise if sharing
                var beacon = new DiscoveryBeacon
                {
                    AppVersion = "1.0.0",
                    PcId = _pcId,
                    PcName = _pcName,
                    JobPort = _jobPort,
                    SharedPrinters = new List<string>(shared)
                };
                var bytes = Encoding.UTF8.GetBytes(beacon.ToJson());
                _beaconSocket.Send(bytes, bytes.Length,
                    new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (Exception ex) { JobLogged?.Invoke($"Beacon error: {ex.Message}"); }
        }

        private async void AcceptLoop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _jobListener.AcceptTcpClientAsync(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => HandleJob(client));
            }
        }

        private void HandleJob(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                string tempPs = null;
                try
                {
                    var headerJson = Encoding.UTF8.GetString(FrameCodec.ReadFrame(stream));
                    var header = JobHeader.FromJson(headerJson);
                    var payload = FrameCodec.ReadFrame(stream);

                    var cfg = _config();
                    if (!string.IsNullOrEmpty(cfg.Pin) && cfg.Pin != header.Pin)
                    {
                        WriteResult(stream, JobResult.Rejected("authentication failed"));
                        JobLogged?.Invoke($"Rejected job from {header.RequestingPc}: bad PIN");
                        return;
                    }
                    if (!IsShared(header.TargetPrinter))
                    {
                        WriteResult(stream, JobResult.Rejected("printer not shared"));
                        return;
                    }

                    WriteResult(stream, JobResult.Accepted());

                    tempPs = Path.Combine(Path.GetTempPath(), $"pb_{header.JobId}.ps");
                    File.WriteAllBytes(tempPs, payload);
                    _printers.PrintPostScript(header.TargetPrinter, tempPs, Math.Max(1, header.Copies));

                    WriteResult(stream, JobResult.Printed());
                    JobLogged?.Invoke(
                        $"Printed job from {header.RequestingUser}@{header.RequestingPc} -> {header.TargetPrinter}");
                }
                catch (Exception ex)
                {
                    try { WriteResult(stream, JobResult.Error(ex.Message)); } catch { }
                    JobLogged?.Invoke($"Job error: {ex.Message}");
                }
                finally
                {
                    if (tempPs != null && File.Exists(tempPs))
                        try { File.Delete(tempPs); } catch { }
                }
            }
        }

        private bool IsShared(string printerName)
        {
            var shared = _sharedPrinters();
            return shared != null && shared.Contains(printerName);
        }

        private static void WriteResult(Stream stream, JobResult result)
        {
            var bytes = Encoding.UTF8.GetBytes(result.ToJson());
            FrameCodec.WriteFrame(stream, bytes);
            stream.Flush();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _beaconTimer?.Dispose();
            try { _jobListener?.Stop(); } catch { }
            _beaconSocket?.Close();
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/PrintBridge.App/PrintBridge.App.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bat
git add src/PrintBridge.App/SharingService.cs
git commit -m "feat: add sharing service (beacon + job listener)"
```

### Task 3.4: DiscoveryService — UDP listener feeding ServerRegistry

**Files:**
- Create: `src/PrintBridge.App/DiscoveryService.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>The "using" half: listens for beacons and maintains a live registry.</summary>
    public class DiscoveryService : IDisposable
    {
        private readonly ServerRegistry _registry =
            new ServerRegistry(TimeSpan.FromSeconds(10));
        private UdpClient _socket;
        private CancellationTokenSource _cts;

        public ServerRegistry Registry => _registry;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _socket = new UdpClient();
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress, true);
            _socket.Client.Bind(new IPEndPoint(IPAddress.Any, SharingService.DiscoveryPort));
            ReceiveLoop();
        }

        private async void ReceiveLoop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await _socket.ReceiveAsync(); }
                catch { break; }

                var json = Encoding.UTF8.GetString(result.Buffer);
                var beacon = DiscoveryBeacon.FromJson(json);
                if (beacon != null)
                    _registry.Observe(beacon, result.RemoteEndPoint.Address.ToString(),
                        DateTime.UtcNow);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _socket?.Close();
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PrintBridge.App/PrintBridge.App.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bat
git add src/PrintBridge.App/DiscoveryService.cs
git commit -m "feat: add discovery service (beacon listener)"
```

### Task 3.5: JobSender — TCP client that ships a captured job

**Files:**
- Create: `src/PrintBridge.App/JobSender.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    public class JobSender
    {
        /// <summary>
        /// Sends a job to a server and returns the final result. Reads two result
        /// frames: the initial Accepted/Rejected, then the terminal Printed/Error.
        /// </summary>
        public JobResult Send(string serverIp, int jobPort, JobHeader header, byte[] postScript,
            int timeoutMs = 30000)
        {
            using (var client = new TcpClient())
            {
                var connect = client.BeginConnect(serverIp, jobPort, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(timeoutMs))
                    return JobResult.Error("server offline");
                client.EndConnect(connect);

                using (var stream = client.GetStream())
                {
                    FrameCodec.WriteFrame(stream, Encoding.UTF8.GetBytes(header.ToJson()));
                    FrameCodec.WriteFrame(stream, postScript);
                    stream.Flush();

                    var first = JobResult.FromJson(
                        Encoding.UTF8.GetString(FrameCodec.ReadFrame(stream)));
                    if (first.Status == JobStatus.Rejected) return first;

                    try
                    {
                        return JobResult.FromJson(
                            Encoding.UTF8.GetString(FrameCodec.ReadFrame(stream)));
                    }
                    catch (IOException) { return JobResult.Error("connection lost during print"); }
                    catch (EndOfStreamException) { return JobResult.Error("connection lost during print"); }
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PrintBridge.App/PrintBridge.App.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Loopback integration test (one Windows PC)**

Write a throwaway xUnit test in `PrintBridge.App.Tests` that starts a
`SharingService` with a **fake** `IPrinterService` (records the call instead of
printing), advertising a printer named "FAKE", then uses `JobSender` against
`127.0.0.1` to send a tiny PostScript byte array, and asserts the returned
`JobResult.Status == JobStatus.Printed` and the fake recorded one job. Keep this
test (mark `[Trait("Category","Integration")]`). This proves the full
beacon-less job path on a single machine.

```csharp
using System.Collections.Generic;
using System.Text;
using System.Threading;
using PrintBridge.App;
using PrintBridge.Protocol;
using PrintBridge.Spooler;
using Xunit;

public class LoopbackJobTests
{
    private class FakePrinter : IPrinterService
    {
        public readonly List<string> Printed = new List<string>();
        public IReadOnlyList<string> ListLocalPrinters() => new[] { "FAKE" };
        public void PrintPostScript(string printer, string path, int copies) => Printed.Add(printer);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SendOverLoopback_PrintsViaService()
    {
        var fake = new FakePrinter();
        IReadOnlyList<string> shared = new[] { "FAKE" };
        using (var server = new SharingService(fake, () => new AppConfig(),
            () => shared, "pc-test", "TEST", jobPort: 49190))
        {
            server.Start();
            Thread.Sleep(200);

            var header = new JobHeader
            {
                JobId = "j1", TargetPrinter = "FAKE", Copies = 1,
                RequestingUser = "u", RequestingPc = "PC"
            };
            var result = new JobSender().Send("127.0.0.1", 49190, header,
                Encoding.ASCII.GetBytes("%!PS fake"));

            Assert.Equal(JobStatus.Printed, result.Status);
            Assert.Single(fake.Printed);
        }
    }
}
```

Run: `dotnet test tests/PrintBridge.App.Tests --filter LoopbackJobTests`
Expected: PASS — 1 passed.

- [ ] **Step 4: Commit**

```bat
git add src/PrintBridge.App/JobSender.cs tests/PrintBridge.App.Tests/LoopbackJobTests.cs
git commit -m "feat: add job sender + loopback integration test"
```

### Task 3.6: MainForm — two-tab GUI + tray icon

**Files:**
- Modify: `src/PrintBridge.App/MainForm.cs` (replace the generated Form)
- Modify: `src/PrintBridge.App/Program.cs`

- [ ] **Step 1: Implement MainForm**

Replace the generated form with a `MainForm` that:
- Builds a `TabControl` with two `TabPage`s: "My Printers" and "Network Printers".
- "My Printers": a `CheckedListBox` populated from `IPrinterService.ListLocalPrinters()`. On check/uncheck, update `AppConfig.SharedPrinters` and `Save()`. Below it, a read-only `ListBox` bound to `SharingService.JobLogged` lines.
- "Network Printers": a `ListView` refreshed every 2s from `DiscoveryService.Registry.GetActive(DateTime.UtcNow)` (columns: PC, Printer, IP). A **Use** button sets `AppConfig.ActiveRemotePcId/ActiveRemotePrinter` and `Save()`, and (Phase 4) re-points the virtual printer.
- A `NotifyIcon` (tray) with a context menu: Show / Exit. Closing the window hides to tray instead of exiting.

```csharp
using System;
using System.Linq;
using System.Windows.Forms;
using PrintBridge.Protocol;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    public class MainForm : Form
    {
        private readonly IPrinterService _printers;
        private readonly DiscoveryService _discovery;
        private readonly SharingService _sharing;
        private readonly AppConfig _config;
        private readonly string _configPath;

        private CheckedListBox _localList;
        private ListBox _log;
        private ListView _remoteList;
        private NotifyIcon _tray;
        private Timer _refreshTimer;

        public MainForm(IPrinterService printers, DiscoveryService discovery,
            SharingService sharing, AppConfig config, string configPath)
        {
            _printers = printers; _discovery = discovery; _sharing = sharing;
            _config = config; _configPath = configPath;
            BuildUi();
            _sharing.JobLogged += line => BeginInvoke((Action)(() => _log.Items.Insert(0, $"{DateTime.Now:T}  {line}")));
        }

        private void BuildUi()
        {
            Text = "PrintBridge";
            Width = 640; Height = 460;

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // Tab 1 — My Printers
            var myTab = new TabPage("My Printers");
            _localList = new CheckedListBox { Dock = DockStyle.Top, Height = 180, CheckOnClick = true };
            foreach (var p in _printers.ListLocalPrinters())
                _localList.Items.Add(p, _config.SharedPrinters.Contains(p));
            _localList.ItemCheck += OnLocalCheck;
            _log = new ListBox { Dock = DockStyle.Fill };
            myTab.Controls.Add(_log);
            myTab.Controls.Add(_localList);
            myTab.Controls.Add(new Label { Text = "Tick printers to share on the network:", Dock = DockStyle.Top });

            // Tab 2 — Network Printers
            var netTab = new TabPage("Network Printers");
            _remoteList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            _remoteList.Columns.Add("PC", 160);
            _remoteList.Columns.Add("Printer", 220);
            _remoteList.Columns.Add("IP", 140);
            var useBtn = new Button { Text = "Use selected printer", Dock = DockStyle.Bottom, Height = 36 };
            useBtn.Click += OnUseClicked;
            netTab.Controls.Add(_remoteList);
            netTab.Controls.Add(useBtn);

            tabs.TabPages.Add(myTab);
            tabs.TabPages.Add(netTab);
            Controls.Add(tabs);

            _tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "PrintBridge" };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; });
            menu.Items.Add("Exit", null, (s, e) => { _tray.Visible = false; Application.Exit(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; };

            _refreshTimer = new Timer { Interval = 2000 };
            _refreshTimer.Tick += (s, e) => RefreshRemote();
            _refreshTimer.Start();
        }

        private void OnLocalCheck(object sender, ItemCheckEventArgs e)
        {
            var name = _localList.Items[e.Index].ToString();
            BeginInvoke((Action)(() =>
            {
                if (e.NewValue == CheckState.Checked)
                {
                    if (!_config.SharedPrinters.Contains(name)) _config.SharedPrinters.Add(name);
                }
                else _config.SharedPrinters.Remove(name);
                _config.Save(_configPath);
            }));
        }

        private void RefreshRemote()
        {
            var servers = _discovery.Registry.GetActive(DateTime.UtcNow);
            _remoteList.BeginUpdate();
            _remoteList.Items.Clear();
            foreach (var s in servers)
                foreach (var printer in s.SharedPrinters)
                {
                    var item = new ListViewItem(s.PcName);
                    item.SubItems.Add(printer);
                    item.SubItems.Add(s.IpAddress);
                    item.Tag = s;
                    _remoteList.Items.Add(item);
                }
            _remoteList.EndUpdate();
        }

        private void OnUseClicked(object sender, EventArgs e)
        {
            if (_remoteList.SelectedItems.Count == 0) return;
            var item = _remoteList.SelectedItems[0];
            var server = (ServerEntry)item.Tag;
            _config.ActiveRemotePcId = server.PcId;
            _config.ActiveRemotePrinter = item.SubItems[1].Text;
            _config.Save(_configPath);
            // Phase 4 hook: VirtualPrinterInstaller.RepointTo(server, printer)
            MessageBox.Show($"Now printing to '{_config.ActiveRemotePrinter}' on {server.PcName}.",
                "PrintBridge");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;     // hide to tray instead of exiting
                Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
```

- [ ] **Step 2: Wire Program.cs (GUI path only for now; --capture added in Phase 4)**

```csharp
using System;
using System.IO;
using System.Windows.Forms;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--capture")
            {
                CaptureMode.Run(args);   // implemented in Phase 4
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var configPath = AppConfig.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            var config = AppConfig.Load(configPath);

            // Ghostscript path resolved relative to the install dir (Phase 5 bundles it).
            var gsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "ghostscript", "bin", "gswin64c.exe");
            IPrinterService printers = File.Exists(gsPath)
                ? (IPrinterService)new GhostscriptPrinter(gsPath)
                : new EnumOnlyPrinterService(); // graceful pre-bundle fallback

            var pcId = MachineIdentity.GetOrCreatePcId();
            var pcName = Environment.MachineName;

            var sharing = new SharingService(printers, () => config,
                () => config.SharedPrinters, pcId, pcName);
            var discovery = new DiscoveryService();
            sharing.Start();
            discovery.Start();

            Application.Run(new MainForm(printers, discovery, sharing, config, configPath));
            sharing.Dispose();
            discovery.Dispose();
        }
    }
}
```

- [ ] **Step 3: Add the two small helpers referenced above**

Create `src/PrintBridge.App/MachineIdentity.cs`:

```csharp
using System;
using System.IO;

namespace PrintBridge.App
{
    /// <summary>Stable per-PC id persisted next to the config.</summary>
    public static class MachineIdentity
    {
        public static string GetOrCreatePcId()
        {
            var path = Path.Combine(Path.GetDirectoryName(AppConfig.DefaultPath), "pcid.txt");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            var id = Guid.NewGuid().ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, id);
            return id;
        }
    }
}
```

Create `src/PrintBridge.App/EnumOnlyPrinterService.cs` (lets the GUI run before
Ghostscript is bundled; printing throws a clear message):

```csharp
using System.Collections.Generic;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    /// <summary>Fallback used before Ghostscript is bundled: lists printers, can't print.</summary>
    public class EnumOnlyPrinterService : IPrinterService
    {
        private readonly PrinterEnumerator _enum = new PrinterEnumerator();
        public IReadOnlyList<string> ListLocalPrinters() => _enum.ListLocalPrinters();
        public void PrintPostScript(string printerName, string postScriptPath, int copies) =>
            throw new System.InvalidOperationException(
                "Ghostscript is not installed yet — run the full installer (Phase 5).");
    }
}
```

- [ ] **Step 4: Build and run the GUI**

Run: `dotnet build src/PrintBridge.App/PrintBridge.App.csproj`
Then run the produced `PrintBridge.App.exe`. Verify: window opens, "My Printers"
lists local printers with checkboxes that persist after restart, "Network
Printers" shows other PCs once a second instance is running on the LAN, tray icon
works, closing hides to tray.

- [ ] **Step 5: Commit**

```bat
git add src/PrintBridge.App
git commit -m "feat: add two-tab GUI, tray, and app wiring"
```

---

## Phase 4 — Virtual printer + capture mode

This is the Approach-B core: install a PostScript virtual printer routed through
mfilemon, and implement `--capture` so the spooled job reaches the running app.

### Task 4.1: Bundle mfilemon and document the install commands

**Files:**
- Create: `third_party/mfilemon/` (drop x64 + x86 `mfilemon.dll` here)
- Create: `installer/install-virtual-printer.ps1`
- Create: `installer/uninstall-virtual-printer.ps1`

- [ ] **Step 1: Obtain mfilemon**

Download the mfilemon binaries (open-source redirection port monitor) and place
`mfilemon.dll` (matching OS bitness) under `third_party/mfilemon/`. Record the
exact version/source URL in `third_party/mfilemon/SOURCE.txt`.

- [ ] **Step 2: Write `install-virtual-printer.ps1`**

The script (run elevated) must:
1. Enable test-signing if not already on (`bcdedit /set testsigning on` — note: requires reboot; the script prints a clear message if a reboot is needed).
2. Install the self-signed test cert (created in Task 4.2) into `LocalMachine\Root` and `TrustedPublisher`.
3. Copy `mfilemon.dll` to `%SystemRoot%\System32\spool\monitors\`.
4. Register the port monitor via `rundll32`/`AddMonitor` (documented inline).
5. Create a port named `PrintBridge:` of type mfilemon whose command runs
   `"<InstallDir>\PrintBridge.App.exe" --capture` and pipes the spool to its stdin.
6. Add a printer named **"Shared Printer (PrintBridge)"** using the in-box
   driver **"Microsoft PS Class Driver"** (or "MS Publisher Imagesetter") bound to
   the `PrintBridge:` port, via `Add-Printer`/`rundll32 printui.dll,PrintUIEntry /if`.

Include the exact `Add-Printer -Name "Shared Printer (PrintBridge)" -DriverName
"Microsoft PS Class Driver" -PortName "PrintBridge:"` line and the `Add-PrinterPort`
/ mfilemon registry entries. Write `uninstall-virtual-printer.ps1` to reverse
every step (remove printer, port, monitor, cert).

- [ ] **Step 3: Manual verification (Windows PC, elevated)**

Run `install-virtual-printer.ps1`. Open *Devices and Printers* and confirm
"Shared Printer (PrintBridge)" exists. Print a test page from Notepad to it and
confirm (via a temporary capture stub) that `--capture` is invoked. Then run the
uninstall script and confirm it's removed.

- [ ] **Step 4: Commit**

```bat
git add installer/install-virtual-printer.ps1 installer/uninstall-virtual-printer.ps1 third_party/mfilemon/SOURCE.txt
git commit -m "feat: add virtual printer install/uninstall scripts"
```

### Task 4.2: Self-signed test certificate generation

**Files:**
- Create: `installer/make-test-cert.ps1`

- [ ] **Step 1: Write the cert script**

Use `New-SelfSignedCertificate` to create a code-signing cert "PrintBridge Test
CA", export the `.cer` (public) and `.pfx` (private, password-protected) to
`installer/certs/`. Document `signtool sign /f PrintBridge.pfx /p <pw> mfilemon.dll`
to sign the port monitor DLL. The install script (Task 4.1) imports the `.cer`
into the trust stores.

- [ ] **Step 2: Run and verify**

Run `make-test-cert.ps1`; confirm `PrintBridge.cer` and `PrintBridge.pfx` are
produced. Sign `mfilemon.dll` and confirm `signtool verify /pa mfilemon.dll`
passes against the imported cert (in test-signing mode).

- [ ] **Step 3: Commit (do NOT commit the .pfx private key)**

Add `installer/certs/*.pfx` to `.gitignore`. Commit only the script and the public `.cer`.

```bat
git add installer/make-test-cert.ps1 installer/certs/PrintBridge.cer .gitignore
git commit -m "feat: add self-signed test certificate generation"
```

### Task 4.3: CaptureMode — stdin PostScript → running app via named pipe

**Files:**
- Create: `src/PrintBridge.App/CaptureMode.cs`
- Modify: `src/PrintBridge.App/Program.cs` (already routes `--capture`)
- Modify: `src/PrintBridge.App/MainForm.cs` / a new `CaptureServer.cs` to receive the piped job

- [ ] **Step 1: Implement a named-pipe receiver in the running app**

Create `src/PrintBridge.App/CaptureServer.cs`: a `NamedPipeServerStream`
("PrintBridge.Capture") loop that, on each connection, reads a frame containing
the PostScript bytes, builds a `JobHeader` from the current `AppConfig`
(`ActiveRemotePrinter`, requesting user/PC, PIN), looks up the active server's IP
from `DiscoveryService.Registry`, and calls `JobSender.Send(...)`, then raises a
`JobLogged`-style event for the tray/log. Start it from `Program.Main` alongside
the other services.

```csharp
using System;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    public class CaptureServer : IDisposable
    {
        public const string PipeName = "PrintBridge.Capture";
        private readonly Func<AppConfig> _config;
        private readonly DiscoveryService _discovery;
        private readonly JobSender _sender = new JobSender();
        private CancellationTokenSource _cts;
        public event Action<string> JobLogged;

        public CaptureServer(Func<AppConfig> config, DiscoveryService discovery)
        { _config = config; _discovery = discovery; }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        private void Loop()
        {
            while (!_cts.IsCancellationRequested)
            {
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    try { pipe.WaitForConnection(); } catch { break; }
                    try
                    {
                        var ps = FrameCodec.ReadFrame(pipe);
                        ForwardJob(ps);
                    }
                    catch (Exception ex) { JobLogged?.Invoke($"Capture error: {ex.Message}"); }
                }
            }
        }

        private void ForwardJob(byte[] postScript)
        {
            var cfg = _config();
            if (string.IsNullOrEmpty(cfg.ActiveRemotePrinter))
            { JobLogged?.Invoke("No remote printer selected — job dropped."); return; }

            var server = _discovery.Registry.GetActive(DateTime.UtcNow)
                .FirstOrDefault(s => s.PcId == cfg.ActiveRemotePcId);
            if (server == null)
            { JobLogged?.Invoke("Selected server is offline — job dropped."); return; }

            var header = new JobHeader
            {
                JobId = Guid.NewGuid().ToString(),
                TargetPrinter = cfg.ActiveRemotePrinter,
                Copies = 1,
                PaperSize = "A4",
                RequestingUser = Environment.UserName,
                RequestingPc = Environment.MachineName,
                Pin = cfg.Pin
            };
            var result = _sender.Send(server.IpAddress, server.JobPort, header, postScript);
            JobLogged?.Invoke($"Sent job to {server.PcName}: {result.Status} {result.Detail}");
        }

        public void Dispose() { _cts?.Cancel(); }
    }
}
```

- [ ] **Step 2: Implement `CaptureMode.Run` (the `--capture` child process)**

```csharp
using System;
using System.IO;
using System.IO.Pipes;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>
    /// Runs when the port monitor spawns us per job. Reads the spooled PostScript
    /// from stdin and forwards it to the already-running GUI app over a named pipe.
    /// </summary>
    public static class CaptureMode
    {
        public static void Run(string[] args)
        {
            using (var stdin = Console.OpenStandardInput())
            using (var buffer = new MemoryStream())
            {
                stdin.CopyTo(buffer);
                var ps = buffer.ToArray();

                using (var pipe = new NamedPipeClientStream(".", CaptureServer.PipeName,
                    PipeDirection.Out))
                {
                    pipe.Connect(5000);
                    FrameCodec.WriteFrame(pipe, ps);
                    pipe.Flush();
                }
            }
        }
    }
}
```

- [ ] **Step 3: Start CaptureServer in Program.Main**

Add after `discovery.Start();`:

```csharp
var capture = new CaptureServer(() => config, discovery);
capture.JobLogged += line => Console.WriteLine(line); // surfaced in MainForm log via event wiring
capture.Start();
```
Wire `capture.JobLogged` into the same `MainForm` log list as `sharing.JobLogged`
(pass `capture` into `MainForm` or subscribe before `Application.Run`).

- [ ] **Step 4: Build**

Run: `dotnet build src/PrintBridge.App/PrintBridge.App.csproj`
Expected: `Build succeeded`.

- [ ] **Step 5: End-to-end capture test (single Windows PC, paperless)**

With the virtual printer installed (Task 4.1) and the app running: set the active
remote printer (Tab 2) to a second instance/loopback whose shared printer is
**"Microsoft Print to PDF"**. Print a Notepad page to "Shared Printer
(PrintBridge)". Confirm the job flows capture → pipe → JobSender → SharingService
→ Print-to-PDF, and a PDF is produced. Confirm the log shows "Printed".

- [ ] **Step 6: Commit**

```bat
git add src/PrintBridge.App/CaptureMode.cs src/PrintBridge.App/CaptureServer.cs src/PrintBridge.App/Program.cs
git commit -m "feat: add capture mode and named-pipe job forwarding"
```

### Task 4.4: VirtualPrinterInstaller.RepointTo (re-point virtual printer on Use)

**Files:**
- Create: `src/PrintBridge.Spooler/VirtualPrinterInstaller.cs`
- Modify: `src/PrintBridge.App/MainForm.cs` (call it from `OnUseClicked`)

- [ ] **Step 1: Implement RepointTo**

Because the active mapping (which remote printer) is stored in `AppConfig` and
read by `CaptureServer` at send time, the virtual printer itself does not need
reconfiguration on every switch. `VirtualPrinterInstaller` instead provides:
`IsInstalled()` (checks the "Shared Printer (PrintBridge)" printer exists) and
`EnsureInstalled()` (invokes `install-virtual-printer.ps1` elevated if missing).
`OnUseClicked` calls `EnsureInstalled()` so first use sets everything up.

```csharp
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;

namespace PrintBridge.Spooler
{
    public class VirtualPrinterInstaller
    {
        public const string VirtualPrinterName = "Shared Printer (PrintBridge)";

        public bool IsInstalled() =>
            PrinterSettings.InstalledPrinters.Cast<string>().Contains(VirtualPrinterName);

        public void EnsureInstalled(string installScriptPath)
        {
            if (IsInstalled()) return;
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{installScriptPath}\"",
                Verb = "runas",          // elevation prompt
                UseShellExecute = true
            };
            Process.Start(psi)?.WaitForExit();
        }
    }
}
```

- [ ] **Step 2: Call from MainForm.OnUseClicked**

After saving config in `OnUseClicked`, add:

```csharp
var installer = new VirtualPrinterInstaller();
var script = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
    "installer", "install-virtual-printer.ps1");
installer.EnsureInstalled(script);
```

- [ ] **Step 3: Build + verify**

Run: `dotnet build`. On a Windows PC, click **Use** with no virtual printer
installed → confirm the elevation prompt runs the script and the printer appears.

- [ ] **Step 4: Commit**

```bat
git add src/PrintBridge.Spooler/VirtualPrinterInstaller.cs src/PrintBridge.App/MainForm.cs
git commit -m "feat: ensure virtual printer installed on first use"
```

---

## Phase 5 — Installer (Inno Setup, bundles everything)

### Task 5.1: Bundle Ghostscript

**Files:**
- Create: `third_party/ghostscript/` (gs binaries: `bin/gswin64c.exe`, `bin/gswin32c.exe`, `lib/`)

- [ ] **Step 1: Add Ghostscript binaries**

Place a redistributable Ghostscript (AGPL or commercial as appropriate) under
`third_party/ghostscript/`. Record version + license in `SOURCE.txt`. Confirm
`gswin64c.exe -h` runs.

- [ ] **Step 2: Commit (respect license; large binaries may use Git LFS)**

```bat
git add third_party/ghostscript/SOURCE.txt
git commit -m "chore: bundle Ghostscript (see SOURCE.txt)"
```

### Task 5.2: Inno Setup script

**Files:**
- Create: `installer/PrintBridge.iss`

- [ ] **Step 1: Write the Inno Setup script**

The script must: install `PrintBridge.App.exe` + dependencies + `third_party/ghostscript`
+ `third_party/mfilemon` + the `installer/*.ps1` scripts + `PrintBridge.cer` into
`{pf}\PrintBridge`; add a Start Menu + optional Run-at-startup entry; on install,
run `install-virtual-printer.ps1` elevated; on uninstall, run
`uninstall-virtual-printer.ps1`. Pin the install dir layout so the app's
`AppDomain.CurrentDomain.BaseDirectory + "ghostscript\bin\gswin64c.exe"` and
`+ "installer\install-virtual-printer.ps1"` paths resolve.

- [ ] **Step 2: Build the installer**

Compile with `iscc installer\PrintBridge.iss`. Expected: `PrintBridge-Setup.exe`
produced.

- [ ] **Step 3: Clean-machine install test**

On a fresh Windows VM/PC: run `PrintBridge-Setup.exe`, accept the test-signing /
reboot prompt, and confirm the app launches, the virtual printer exists, and
Ghostscript path resolves (printing no longer throws the fallback error).

- [ ] **Step 4: Commit**

```bat
git add installer/PrintBridge.iss
git commit -m "feat: add Inno Setup installer bundling app, Ghostscript, mfilemon"
```

---

## Phase 6 — End-to-end hardening + Windows version matrix

### Task 6.1: Real two-PC print test

- [ ] **Step 1: Install on both PCs**

Install `PrintBridge-Setup.exe` on PC-A (with a real printer) and PC-B.

- [ ] **Step 2: Share + discover**

On PC-A, tick the real printer in "My Printers". On PC-B, confirm it appears in
"Network Printers" within ~5s. Click **Use**.

- [ ] **Step 3: Print from a real app**

On PC-B, open a PDF in a browser → Print → "Shared Printer (PrintBridge)".
Confirm a page prints on PC-A's printer and PC-B's log shows "Printed".

- [ ] **Step 4: Record results**

Document success in `docs/superpowers/plans/RESULTS.md` (PC names, Windows
versions, printer model, pass/fail).

### Task 6.2: Negative-path verification

- [ ] **Step 1: PIN mismatch** — set a PIN on PC-A; send from PC-B with no/ wrong PIN; confirm "authentication failed" tray notice.
- [ ] **Step 2: Server offline** — close PrintBridge on PC-A mid-list; confirm PC-B shows the printer disappear within ~10s and a send yields "server offline".
- [ ] **Step 3: Printer offline** — power off PC-A's printer; send; confirm a clear printer-error result.
- [ ] **Step 4: Commit results**

```bat
git add docs/superpowers/plans/RESULTS.md
git commit -m "test: record end-to-end and negative-path results"
```

### Task 6.3: Windows version matrix

- [ ] **Step 1:** Repeat Task 6.1 across each available Windows version pairing (10↔11 minimum; include 7/8 if hardware exists). For Windows 7, confirm .NET Framework 4.8 is installed (ship the offline installer link in README).
- [ ] **Step 2:** Note any per-version quirks (driver name differences — e.g. "Microsoft PS Class Driver" may be named differently pre-Win10; the install script must pick an available in-box PS driver) in `RESULTS.md`.
- [ ] **Step 3: Tray notifications polish** — convert key `JobLogged` events (Printed / Error / auth failed) into `NotifyIcon.ShowBalloonTip` toasts so users get feedback without opening the window. Commit.

```bat
git add src/PrintBridge.App docs/superpowers/plans/RESULTS.md
git commit -m "feat: tray notifications + cross-version verification results"
```

---

## Self-Review Notes (author)

- **Spec coverage:** share printers (Tab 1 + SharingService beacon), use remote
  printers (Tab 2 + DiscoveryService + virtual printer + capture), driver-free
  clients (PostScript via in-box driver + Ghostscript on server), LAN discovery
  (UDP beacon), optional PIN auth (SharingService check), Win7–11 (.NET 4.8),
  self-signed/test-signing (Task 4.2), error handling (JobResult + tray),
  installer (Phase 5), two-PC E2E (Phase 6) — all mapped to tasks.
- **Single-app requirement:** one exe, `--capture` is a mode, both roles run
  together (Program.Main starts SharingService + DiscoveryService + CaptureServer).
- **Type consistency:** `IPrinterService` (ListLocalPrinters/PrintPostScript),
  `DiscoveryBeacon`, `JobHeader`, `JobResult`/`JobStatus`, `ServerRegistry.Observe/GetActive`,
  `SharingService.DiscoveryPort/DefaultJobPort`, `CaptureServer.PipeName`,
  `VirtualPrinterInstaller.VirtualPrinterName` are used consistently across tasks.
- **Known integration risks deferred to verification:** exact in-box PostScript
  driver name varies by Windows version (handled in Task 6.3); mfilemon registry
  registration specifics are filled in during Task 4.1 on the actual OS.
```
