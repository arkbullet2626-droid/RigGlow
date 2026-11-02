using System.Net.Sockets;
using System.Text;

namespace RigGlow;

/// <summary>A simple 24-bit colour.</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R, G, B;
    public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
    public Rgb(int r, int g, int b)
    {
        R = (byte)Math.Clamp(r, 0, 255);
        G = (byte)Math.Clamp(g, 0, 255);
        B = (byte)Math.Clamp(b, 0, 255);
    }

    public static readonly Rgb Black = new Rgb(0, 0, 0);
    public static readonly Rgb White = new Rgb(255, 255, 255);

    public static Rgb FromHex(string hex)
    {
        try
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length != 6) return White;
            return new Rgb(Convert.ToByte(hex.Substring(0, 2), 16),
                           Convert.ToByte(hex.Substring(2, 2), 16),
                           Convert.ToByte(hex.Substring(4, 2), 16));
        }
        catch { return White; }
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    public Color ToColor() => Color.FromArgb(R, G, B);
    public static Rgb FromColor(Color c) => new Rgb(c.R, c.G, c.B);

    public Rgb Scale(double f)
    {
        f = Math.Clamp(f, 0, 1);
        return new Rgb((int)Math.Round(R * f), (int)Math.Round(G * f), (int)Math.Round(B * f));
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb((int)Math.Round(a.R + (b.R - a.R) * t),
                       (int)Math.Round(a.G + (b.G - a.G) * t),
                       (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>h in degrees (any value), s and v 0..1.</summary>
    public static Rgb FromHsv(double h, double s, double v)
    {
        h %= 360; if (h < 0) h += 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return new Rgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;
    public override bool Equals(object obj) => obj is Rgb o && Equals(o);
    public override int GetHashCode() => (R << 16) | (G << 8) | B;
}

public sealed class RgbZone
{
    public string Name;
    public int Type;          // 0 = single, 1 = linear, 2 = matrix
    public int LedsMin, LedsMax, LedsCount;
    public int StartIndex;    // index of this zone's first LED within the device
    public bool Resizable => LedsMax > LedsMin;
}

public sealed class RgbDevice
{
    public int Index;
    public int Type;
    public string Name = "";
    public string Vendor = "";
    public string Description = "";
    public string Location = "";
    public int ActiveMode;
    public List<string> Modes = new();
    public List<RgbZone> Zones = new();
    public int LedCount;
    /// <summary>For every LED, its position 0..1 inside its own zone (used by wave effects).</summary>
    public double[] Positions = Array.Empty<double>();

    /// <summary>Stable key used to remember settings for this device between runs.</summary>
    public string Key => $"{Name}|{Location}";

    public string TypeName => Type switch
    {
        0 => "Motherboard",
        1 => "RAM",
        2 => "GPU",
        3 => "Cooler",
        4 => "LED strip",
        5 => "Keyboard",
        6 => "Mouse",
        7 => "Mousemat",
        8 => "Headset",
        9 => "Headset stand",
        10 => "Gamepad",
        11 => "Light",
        12 => "Speaker",
        13 => "Virtual",
        14 => "Storage",
        15 => "Case",
        _ => "Device"
    };

    public override string ToString() => $"{Name}  ({TypeName}, {LedCount} LEDs)";
}

/// <summary>
/// Minimal client for the OpenRGB SDK network protocol (default port 6742).
/// OpenRGB does the actual hardware talking; we only send colours.
/// </summary>
public sealed class OpenRgbClient : IDisposable
{
    const uint PKT_REQUEST_CONTROLLER_COUNT = 0;
    const uint PKT_REQUEST_CONTROLLER_DATA = 1;
    const uint PKT_REQUEST_PROTOCOL_VERSION = 40;
    const uint PKT_SET_CLIENT_NAME = 50;
    const uint PKT_DEVICE_LIST_UPDATED = 100;
    const uint PKT_RESIZEZONE = 1000;
    const uint PKT_UPDATELEDS = 1050;
    const uint PKT_SETCUSTOMMODE = 1100;

    // Protocol 3 is understood by OpenRGB 0.7 and newer (including 1.0). The server
    // answers in whichever version we ask for, so we keep the parser to one known layout.
    const uint ClientProtocol = 3;

    TcpClient _tcp;
    NetworkStream _stream;
    readonly object _io = new();

    public uint Protocol { get; private set; }
    public bool Connected => _tcp != null && _tcp.Connected;
    public volatile bool DeviceListChanged;

    public void Connect(string host, int port, int timeoutMs = 1500)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            var task = tcp.ConnectAsync(host, port);
            if (!task.Wait(timeoutMs) || !tcp.Connected)
                throw new IOException("OpenRGB SDK server did not answer on " + host + ":" + port);
        }
        catch (AggregateException ae)
        {
            tcp.Dispose();
            throw new IOException(ae.InnerException?.Message ?? ae.Message);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        _tcp = tcp;
        _stream = tcp.GetStream();
        _stream.ReadTimeout = 5000;
        _stream.WriteTimeout = 5000;

        Send(0, PKT_SET_CLIENT_NAME, Encoding.ASCII.GetBytes("RigGlow\0"));
        Send(0, PKT_REQUEST_PROTOCOL_VERSION, BitConverter.GetBytes(ClientProtocol));
        var reply = Receive(PKT_REQUEST_PROTOCOL_VERSION);
        uint server = reply.Length >= 4 ? BitConverter.ToUInt32(reply, 0) : 0;
        Protocol = Math.Min(ClientProtocol, server);
    }

    public int GetControllerCount()
    {
        Send(0, PKT_REQUEST_CONTROLLER_COUNT, Array.Empty<byte>());
        var r = Receive(PKT_REQUEST_CONTROLLER_COUNT);
        return (int)BitConverter.ToUInt32(r, 0);
    }

    public RgbDevice GetController(int index)
    {
        var payload = Protocol > 0 ? BitConverter.GetBytes(Protocol) : Array.Empty<byte>();
        Send((uint)index, PKT_REQUEST_CONTROLLER_DATA, payload);
        var data = Receive(PKT_REQUEST_CONTROLLER_DATA);
        var d = ParseController(data, Protocol);
        d.Index = index;
        return d;
    }

    /// <summary>Puts the device into its "Direct"/custom mode so we can stream colours.</summary>
    public void SetCustomMode(int index) => Send((uint)index, PKT_SETCUSTOMMODE, Array.Empty<byte>());

    public void ResizeZone(int index, int zone, int newSize)
    {
        var buf = new byte[8];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), zone);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), newSize);
        Send((uint)index, PKT_RESIZEZONE, buf);
    }

    public void UpdateLeds(int index, Rgb[] colors)
    {
        int n = colors.Length;
        var buf = new byte[4 + 2 + 4 * n];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), (uint)buf.Length);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 2), (ushort)n);
        for (int i = 0; i < n; i++)
        {
            int o = 6 + i * 4;
            buf[o] = colors[i].R;
            buf[o + 1] = colors[i].G;
            buf[o + 2] = colors[i].B;
            buf[o + 3] = 0;
        }
        Send((uint)index, PKT_UPDATELEDS, buf);
    }

    /// <summary>Reads any packets the server pushed on its own (e.g. "device list changed").</summary>
    public void Poll()
    {
        lock (_io)
        {
            while (_tcp != null && _tcp.Available >= 16)
            {
                ReadPacket(out _, out uint id, out _);
                if (id == PKT_DEVICE_LIST_UPDATED) DeviceListChanged = true;
            }
        }
    }

    void Send(uint device, uint packetId, byte[] data)
    {
        var buf = new byte[16 + data.Length];
        buf[0] = (byte)'O'; buf[1] = (byte)'R'; buf[2] = (byte)'G'; buf[3] = (byte)'B';
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), device);
        BitConverter.TryWriteBytes(buf.AsSpan(8, 4), packetId);
        BitConverter.TryWriteBytes(buf.AsSpan(12, 4), (uint)data.Length);
        Buffer.BlockCopy(data, 0, buf, 16, data.Length);
        lock (_io) _stream.Write(buf, 0, buf.Length);
    }

    byte[] Receive(uint expectedId)
    {
        lock (_io)
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                var data = ReadPacket(out _, out uint id, out _);
                if (id == expectedId) return data;
                if (id == PKT_DEVICE_LIST_UPDATED) DeviceListChanged = true;
            }
        }
        throw new IOException("OpenRGB did not send the expected reply.");
    }

    byte[] ReadPacket(out uint device, out uint id, out uint size)
    {
        var header = ReadExact(16);
        if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B')
            throw new IOException("Bad packet from OpenRGB.");
        device = BitConverter.ToUInt32(header, 4);
        id = BitConverter.ToUInt32(header, 8);
        size = BitConverter.ToUInt32(header, 12);
        if (size > 16 * 1024 * 1024) throw new IOException("Packet from OpenRGB too large.");
        return ReadExact((int)size);
    }

    byte[] ReadExact(int count)
    {
        var buf = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = _stream.Read(buf, got, count - got);
            if (n <= 0) throw new IOException("OpenRGB closed the connection.");
            got += n;
        }
        return buf;
    }

    static string ReadString(BinaryReader br)
    {
        ushort len = br.ReadUInt16();
        if (len == 0) return "";
        var bytes = br.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    static RgbDevice ParseController(byte[] data, uint protocol)
    {
        var d = new RgbDevice();
        using var br = new BinaryReader(new MemoryStream(data));
        br.ReadUInt32();                 // data size
        d.Type = br.ReadInt32();
        d.Name = ReadString(br);
        if (protocol >= 1) d.Vendor = ReadString(br);
        d.Description = ReadString(br);
        ReadString(br);                  // version
        ReadString(br);                  // serial
        d.Location = ReadString(br);

        ushort numModes = br.ReadUInt16();
        d.ActiveMode = br.ReadInt32();
        for (int m = 0; m < numModes; m++)
        {
            d.Modes.Add(ReadString(br));
            br.ReadInt32();              // value
            br.ReadUInt32();             // flags
            br.ReadUInt32();             // speed min
            br.ReadUInt32();             // speed max
            if (protocol >= 3) { br.ReadUInt32(); br.ReadUInt32(); } // brightness min/max
            br.ReadUInt32();             // colors min
            br.ReadUInt32();             // colors max
            br.ReadUInt32();             // speed
            if (protocol >= 3) br.ReadUInt32(); // brightness
            br.ReadUInt32();             // direction
            br.ReadUInt32();             // color mode
            ushort nc = br.ReadUInt16();
            br.ReadBytes(nc * 4);
        }

        ushort numZones = br.ReadUInt16();
        int start = 0;
        for (int z = 0; z < numZones; z++)
        {
            var zone = new RgbZone
            {
                Name = ReadString(br),
                Type = br.ReadInt32(),
                LedsMin = (int)br.ReadUInt32(),
                LedsMax = (int)br.ReadUInt32(),
                LedsCount = (int)br.ReadUInt32(),
            };
            ushort matrixLen = br.ReadUInt16();
            if (matrixLen > 0) br.ReadBytes(matrixLen);
            if (protocol >= 4)
            {
                ushort segs = br.ReadUInt16();
                for (int s = 0; s < segs; s++) { ReadString(br); br.ReadInt32(); br.ReadUInt32(); br.ReadUInt32(); }
            }
            zone.StartIndex = start;
            start += zone.LedsCount;
            d.Zones.Add(zone);
        }

        ushort numLeds = br.ReadUInt16();
        for (int l = 0; l < numLeds; l++) { ReadString(br); br.ReadUInt32(); }
        d.LedCount = numLeds;

        // Work out each LED's position within its zone for wave effects.
        d.Positions = new double[numLeds];
        for (int i = 0; i < numLeds; i++) d.Positions[i] = numLeds > 1 ? i / (double)numLeds : 0;
        foreach (var zone in d.Zones)
            for (int j = 0; j < zone.LedsCount; j++)
            {
                int i = zone.StartIndex + j;
                if (i < numLeds) d.Positions[i] = zone.LedsCount > 1 ? j / (double)zone.LedsCount : 0;
            }
        return d;
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
    }
}
