using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Coreloom.Core.Connect;

namespace Coreloom.Core.Tests;

public class ConnectCryptoTests
{
    [Fact]
    public void Base64UrlRoundTripsWithoutPaddingOrUnsafeCharacters()
    {
        byte[] bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        for (int length = 0; length <= 40; length++)
        {
            string text = ConnectCrypto.ToBase64Url(bytes.AsSpan(200, length));
            Assert.DoesNotContain("+", text);
            Assert.DoesNotContain("/", text);
            Assert.DoesNotContain("=", text);
            if (length > 0)
            {
                Assert.Equal(bytes.AsSpan(200, length).ToArray(), ConnectCrypto.FromBase64Url(text));
            }
        }

        Assert.Null(ConnectCrypto.FromBase64Url(null));
        Assert.Null(ConnectCrypto.FromBase64Url(string.Empty));
        Assert.Null(ConnectCrypto.FromBase64Url("not valid!"));
        Assert.Null(ConnectCrypto.FromBase64Url("abcde")); // a length base64 can't have
    }

    [Fact]
    public void SealedDataOpensOnlyWithTheSameKeyAndContext()
    {
        byte[] key = ConnectCrypto.RandomBytes(ConnectCrypto.KeySize);
        byte[] sealedBytes = ConnectCrypto.Seal(key, "hello"u8, "coreloom/test");
        Assert.Equal(ConnectCrypto.NonceSize + 5 + ConnectCrypto.TagSize, sealedBytes.Length);
        Assert.Equal("hello"u8.ToArray(), ConnectCrypto.Open(key, sealedBytes, "coreloom/test"));

        Assert.Null(ConnectCrypto.Open(key, sealedBytes, "coreloom/other"));
        Assert.Null(ConnectCrypto.Open(ConnectCrypto.RandomBytes(ConnectCrypto.KeySize), sealedBytes, "coreloom/test"));
        byte[] tampered = (byte[])sealedBytes.Clone();
        tampered[ConnectCrypto.NonceSize] ^= 1;
        Assert.Null(ConnectCrypto.Open(key, tampered, "coreloom/test"));
        Assert.Null(ConnectCrypto.Open(key, sealedBytes.AsSpan(0, 20), "coreloom/test"));

        // A fresh nonce each time.
        Assert.NotEqual(Convert.ToHexString(sealedBytes), Convert.ToHexString(ConnectCrypto.Seal(key, "hello"u8, "coreloom/test")));
    }

    [Fact]
    public void PhoneAndComputerAgreeOnTheSharedSecret()
    {
        using var phone = new ConnectCrypto.EphemeralKey();
        using var pc = new ConnectCrypto.EphemeralKey();
        Assert.Equal(ConnectCrypto.PublicKeySize, phone.PublicKey.Length);
        Assert.Equal(0x04, phone.PublicKey[0]);

        byte[]? a = phone.SharedSecret(pc.PublicKey);
        byte[]? b = pc.SharedSecret(phone.PublicKey);
        Assert.NotNull(a);
        Assert.Equal(32, a!.Length);
        Assert.Equal(a, b);

        Assert.Null(pc.SharedSecret(null));
        Assert.Null(pc.SharedSecret(new byte[64]));
        byte[] offCurve = new byte[65];
        offCurve[0] = 0x04;
        offCurve[1] = 1;
        offCurve[33] = 1;
        Assert.Null(pc.SharedSecret(offCurve));
    }

    /// <summary>Values computed by the phone app's own cryptography (src/Coreloom.Phone/vendor/crypto.js), so both sides stay in step.</summary>
    [Fact]
    public void KeysMatchThePhoneApps()
    {
        byte[] shared = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        byte[] phone = Enumerable.Range(0, 65).Select(i => (byte)(i == 0 ? 4 : i)).ToArray();
        byte[] pc = Enumerable.Range(0, 65).Select(i => (byte)(i == 0 ? 4 : 100 + i)).ToArray();
        var (encrypt, mac) = ConnectCrypto.PairingKeys(shared, "123456"u8.ToArray(), phone, pc);
        Assert.Equal("1684135eeb18d2455348f42db59723a9d8e2fbd6b6c8e84b8456fb81900ffa61", Convert.ToHexString(encrypt).ToLowerInvariant());
        Assert.Equal("dd8303c197362152357d8868ccc84b0cdda12208ca2d1fe08d7e77ee99baec42", Convert.ToHexString(mac).ToLowerInvariant());
        Assert.Equal("6758b34271f4cf971f8467131a704d85977d1e82b5a367fd4390c0267c5876b0", Convert.ToHexString(ConnectCrypto.Mac(mac, "coreloom/pair/phone")).ToLowerInvariant());

        byte[] device = Enumerable.Range(0, 32).Select(i => (byte)(200 - i)).ToArray();
        byte[] phoneNonce = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        byte[] pcNonce = Enumerable.Range(0, 16).Select(i => (byte)(50 + i)).ToArray();
        var (sessionEncrypt, sessionMac) = ConnectCrypto.SessionKeys(device, phoneNonce, pcNonce);
        Assert.Equal("e21333ac8f7dc5d05d35f996795714eae0fb4ab24f925ffe3c8e3e3297ae940a", Convert.ToHexString(sessionEncrypt).ToLowerInvariant());
        Assert.Equal("5d9627380c9af532e91d9983c46172b22fab83ab7aad890f0e437ee615746bd2", Convert.ToHexString(sessionMac).ToLowerInvariant());
        Assert.Equal("E5A-r6IG6299W1Fsmq3rjyAM6kN3YkveNoupugMG5JM", ConnectCrypto.ToBase64Url(ConnectCrypto.Mac(sessionMac, "/api/live\n{\"session\":\"abc\",\"n\":1}")));
    }

    [Fact]
    public void VerifyNeedsTheWholeMac()
    {
        byte[] mac = ConnectCrypto.Mac(ConnectCrypto.RandomBytes(32), "x");
        Assert.True(ConnectCrypto.Verify(mac, (byte[])mac.Clone()));
        Assert.False(ConnectCrypto.Verify(mac, null));
        Assert.False(ConnectCrypto.Verify(mac, mac[..16]));
        byte[] wrong = (byte[])mac.Clone();
        wrong[^1] ^= 0x80;
        Assert.False(ConnectCrypto.Verify(mac, wrong));
    }
}

public class QrCodeTests
{
    [Fact]
    public void PicksTheSmallestVersionThatFits()
    {
        Assert.Equal(1, QrCode.Encode(new string('a', 14)).Version); // 1-M holds 14 bytes
        Assert.Equal(2, QrCode.Encode(new string('a', 15)).Version);
        Assert.Equal(3, QrCode.Encode(new string('a', 27)).Version); // 2-M holds 26
        Assert.Equal(40, QrCode.Encode(new string('a', 2331)).Version); // 40-M holds 2331
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('a', 2332)));
    }

    [Fact]
    public void UsesStrongerErrorCorrectionWhenItFitsTheSameVersion()
    {
        var qr = QrCode.Encode("abcdefg"); // 1-H holds 7 bytes
        Assert.Equal(1, qr.Version);
        Assert.Equal(QrErrorCorrection.High, qr.ErrorCorrection);
        Assert.Equal(QrErrorCorrection.Quartile, QrCode.Encode("abcdefghijk").ErrorCorrection); // 1-Q holds 11
        Assert.Equal(QrErrorCorrection.Medium, QrCode.Encode(new string('a', 14)).ErrorCorrection);
    }

    [Theory]
    [InlineData("http://192.168.1.24:47101/#p=Zm9vYmFyYmF6cXV4cXV1eGNvcmVsb29tY29ubmVjdA")]
    [InlineData("a")]
    [InlineData("http://[fd00::1234:5678]:47101/#p=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void HasTheFixedPatternsAndTheRightFormatBits(string text)
    {
        var qr = QrCode.Encode(text);
        int size = qr.Size;
        Assert.Equal((qr.Version * 4) + 17, size);

        // Finder patterns in three corners: a dark 7×7 ring, a light ring, a dark 3×3 centre.
        foreach (var (left, top) in new[] { (0, 0), (size - 7, 0), (0, size - 7) })
        {
            for (int dy = 0; dy < 7; dy++)
            {
                for (int dx = 0; dx < 7; dx++)
                {
                    int ring = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    Assert.Equal(ring != 2, qr[left + dx, top + dy]);
                }
            }
        }

        // Timing patterns alternate between the finders.
        for (int i = 8; i < size - 8; i++)
        {
            Assert.Equal(i % 2 == 0, qr[i, 6]);
            Assert.Equal(i % 2 == 0, qr[6, i]);
        }

        // The dark module, and both copies of the format information (error correction and mask, BCH-coded).
        Assert.True(qr[8, size - 8]);
        int eccBits = qr.ErrorCorrection switch
        {
            QrErrorCorrection.Low => 1,
            QrErrorCorrection.Medium => 0,
            QrErrorCorrection.Quartile => 3,
            _ => 2,
        };
        int data = (eccBits << 3) | qr.Mask;
        int rem = data;
        for (int i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ (((rem >> 9) & 1) * 0x537);
        }

        int format = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((format >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++)
        {
            Assert.Equal(Bit(i), qr[8, i]);
        }

        Assert.Equal(Bit(6), qr[8, 7]);
        Assert.Equal(Bit(7), qr[8, 8]);
        Assert.Equal(Bit(8), qr[7, 8]);
        for (int i = 9; i < 15; i++)
        {
            Assert.Equal(Bit(i), qr[14 - i, 8]);
        }

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(Bit(i), qr[size - 1 - i, 8]);
        }

        for (int i = 8; i < 15; i++)
        {
            Assert.Equal(Bit(i), qr[8, size - 15 + i]);
        }
    }

    [Fact]
    public void PathDataDrawsEveryDarkModuleInsideTheQuietZone()
    {
        var qr = QrCode.Encode("http://192.168.1.24:47101/#p=abc");
        string path = qr.ToSvgPath(border: 2);
        Assert.StartsWith("M", path);

        // Rebuild the picture from the path's runs and compare it with the modules.
        var drawn = new bool[qr.Size + 4, qr.Size + 4];
        foreach (var run in path.Split('M', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = run.Replace("h", " h ").Replace("v", " v ").Replace("z", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int x = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            int y = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            int width = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < width; i++)
            {
                drawn[y, x + i] = true;
            }
        }

        for (int y = 0; y < qr.Size + 4; y++)
        {
            for (int x = 0; x < qr.Size + 4; x++)
            {
                Assert.Equal(qr[x - 2, y - 2], drawn[y, x]);
            }
        }
    }
}

public class PairingWindowTests
{
    private DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpensWithALinkSecretAndASixDigitCodeAndRenewsThem()
    {
        var window = new PairingWindow(() => _now);
        Assert.False(window.IsOpen);
        Assert.Null(window.Current);
        Assert.False(window.Accepts(PairingKind.Code));

        window.Open();
        var ticket = window.Current!;
        Assert.Matches("^[0-9]{6}$", ticket.Code);
        Assert.Equal(32, ConnectCrypto.FromBase64Url(ticket.LinkSecret)!.Length);
        Assert.Equal(_now + PairingWindow.Lifetime, ticket.Expires);
        Assert.True(window.Accepts(PairingKind.Link));
        Assert.True(window.Accepts(PairingKind.Code));

        _now += PairingWindow.Lifetime;
        var renewed = window.Current!;
        Assert.NotEqual(ticket.LinkSecret, renewed.LinkSecret);

        window.Renew();
        Assert.NotEqual(renewed.LinkSecret, window.Current!.LinkSecret);

        window.Close();
        Assert.False(window.IsOpen);
        Assert.False(window.Accepts(PairingKind.Link));
    }

    [Fact]
    public void AUsedSecretIsReplaced()
    {
        var window = new PairingWindow(() => _now);
        window.Open();
        var ticket = window.Current!;
        window.Used(ConnectCrypto.FromBase64Url(ticket.LinkSecret)!);
        Assert.NotEqual(ticket.LinkSecret, window.Current!.LinkSecret);
        Assert.NotEqual(ticket.Code + ticket.LinkSecret, window.Current!.Code + window.Current!.LinkSecret);
    }

    [Fact]
    public void FiveWrongCodesReplaceTheCodeAndTenFailuresPause()
    {
        var window = new PairingWindow(() => _now);
        window.Open();
        string code = window.Current!.Code;
        for (int i = 0; i < PairingWindow.CodeAttempts - 1; i++)
        {
            window.Failed(PairingKind.Code);
        }

        Assert.Equal(code, window.Current!.Code);
        window.Failed(PairingKind.Code);
        Assert.NotEqual(code, window.Current!.Code);
        Assert.False(window.IsPaused);

        for (int i = PairingWindow.CodeAttempts; i < PairingWindow.FailuresPerMinute; i++)
        {
            window.Failed(PairingKind.Link);
        }

        Assert.True(window.IsPaused);
        Assert.Empty(window.Candidates(PairingKind.Code));
        Assert.Empty(window.Candidates(PairingKind.Link));

        _now += TimeSpan.FromMinutes(1);
        Assert.False(window.IsPaused);
        Assert.Single(window.Candidates(PairingKind.Code));
    }

    [Fact]
    public void SetupLinksWorkWhilePairingIsClosedAndExpire()
    {
        var window = new PairingWindow(() => _now);
        var links = Enumerable.Range(0, PairingWindow.MaximumSetupLinks + 1).Select(_ => window.MintSetupLink()!).ToList();
        var candidates = window.Candidates(PairingKind.Link);
        Assert.Equal(PairingWindow.MaximumSetupLinks, candidates.Count); // the oldest made way
        Assert.False(window.IsSetupLink(ConnectCrypto.FromBase64Url(links[0])!));
        byte[] newest = ConnectCrypto.FromBase64Url(links[^1])!;
        Assert.True(window.IsSetupLink(newest));
        Assert.False(window.Accepts(PairingKind.Code)); // a setup link isn't a code

        window.Used(newest);
        Assert.False(window.IsSetupLink(newest));

        _now += PairingWindow.SetupLinkLifetime;
        Assert.Empty(window.Candidates(PairingKind.Link));
    }
}

public class PairedDeviceStoreTests
{
    [Fact]
    public void KeepsPhonesAcrossRestartsAndRemovesThem()
    {
        string folder = Path.Combine(Path.GetTempPath(), "coreloom-connect-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(folder, "connect.json");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var store = new PairedDeviceStore(file);
            string pcId = store.PcId;
            Assert.False(string.IsNullOrEmpty(pcId));
            var phone = store.Add("  Jerry's iPhone\u0007 ", "qr", now);
            Assert.Equal("Jerry's iPhone", phone.Name);
            Assert.Equal(32, ConnectCrypto.FromBase64Url(phone.Key)!.Length);
            store.Add(new string('x', 80), "code", now);
            Assert.Equal(40, store.Devices[1].Name.Length);
            Assert.Equal("Phone", PairedDeviceStore.CleanName("   "));

            var reloaded = new PairedDeviceStore(file);
            Assert.Equal(pcId, reloaded.PcId);
            Assert.Equal(2, reloaded.Devices.Count);
            Assert.Equal(phone.Key, reloaded.Find(phone.Id)!.Key);
            Assert.Null(reloaded.Find("nobody"));
            Assert.Null(reloaded.Find(null));

            Assert.True(reloaded.Remove(phone.Id));
            Assert.False(reloaded.Remove(phone.Id));
            Assert.Single(new PairedDeviceStore(file).Devices);
            reloaded.RemoveAll();
            Assert.Empty(new PairedDeviceStore(file).Devices);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void KeepsAtMostTwentyPhones()
    {
        var store = new PairedDeviceStore(null);
        var start = DateTimeOffset.UtcNow;
        for (int i = 0; i < PairedDeviceStore.MaximumDevices + 3; i++)
        {
            store.Add($"Phone {i}", "qr", start.AddMinutes(i));
        }

        Assert.Equal(PairedDeviceStore.MaximumDevices, store.Devices.Count);
        Assert.DoesNotContain(store.Devices, d => d.Name == "Phone 0"); // the longest unseen went first
        Assert.Contains(store.Devices, d => d.Name == $"Phone {PairedDeviceStore.MaximumDevices + 2}");
    }
}

public class ConnectNetworkTests
{
    [Fact]
    public void ThePhoneAppIsBuiltIn()
    {
        var assets = WebAssets.Embedded();
        foreach (string path in new[] { "index.html", "app.js", "app.css", "connect.js", "gauge.js", "chart.js", "vendor/crypto.js", "manifest.webmanifest", "icons/apple-touch-icon.png", "fonts/Inter-Regular.woff2" })
        {
            Assert.True(assets.TryGet("/" + path, out var asset), path);
            Assert.NotEmpty(asset.Body);
        }

        Assert.True(assets.TryGet("/", out var index));
        Assert.Equal("text/html; charset=utf-8", index.ContentType);
        Assert.DoesNotContain(assets.Paths, p => p.Contains('\\') || p.EndsWith("README.md", StringComparison.Ordinal));
        Assert.True(assets.TryGet("/app.js", out var app));
        Assert.NotNull(app.Gzip); // big text files are sent compressed
        Assert.Equal("text/javascript; charset=utf-8", app.ContentType);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.0.0.8", true)]
    [InlineData("172.16.4.1", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.24", true)]
    [InlineData("169.254.10.2", true)]
    [InlineData("100.84.12.7", true)]   // carrier-grade NAT and Tailscale
    [InlineData("100.128.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("192.0.2.2", false)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:4860::8888", false)]
    [InlineData("::ffff:192.168.0.9", true)]
    [InlineData("::ffff:1.1.1.1", false)]
    public void OnlyAnswersPrivateAddresses(string address, bool local) =>
        Assert.Equal(local, MiniHttpServer.IsLocalNetwork(IPAddress.Parse(address)));
}

/// <summary>Coreloom Connect over loopback, with a phone written here from the protocol's description.</summary>
public sealed class ConnectServerTests
{
    private PairedDeviceStore _store = null!;
    private PairingWindow _pairing = null!;
    private HttpClient _http = null!;
    private ConnectServer _server = null!;

    private string BaseUrl => $"http://127.0.0.1:{_server.Port}";

    /// <summary>Starts a server on a free loopback port; dispose it at the end of the test.</summary>
    private IAsyncDisposable Start()
    {
        _store = new PairedDeviceStore(null);
        _pairing = new PairingWindow();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _server = ConnectServer.Start(new ConnectServerOptions
        {
            Port = 0,
            ComputerName = "JERRY-PC",
            Platform = "windows",
            AppVersion = "1.7.0",
            Store = _store,
            Pairing = _pairing,
            Assets = new WebAssets(new Dictionary<string, byte[]>
            {
                ["index.html"] = "<!doctype html><title>Coreloom</title>"u8.ToArray(),
                ["app.js"] = Encoding.UTF8.GetBytes("console.log('" + new string('x', 4000) + "');"),
            }),
            LongPoll = TimeSpan.FromSeconds(1),
        });
        return new Stopper(this);
    }

    private sealed class Stopper(ConnectServerTests owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            owner._http.Dispose();
            await owner._server.DisposeAsync();
        }
    }

    [Fact]
    public async Task ServesTheAppWithItsSecurityHeaders()
    {
        await using var running = Start();
        using var index = await _http.GetAsync(BaseUrl + "/");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("default-src 'self'", index.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", index.Headers.GetValues("X-Frame-Options").Single());
        string etag = index.Headers.ETag!.Tag;

        using var again = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/index.html");
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        using var notModified = await _http.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);

        using var missing = await _http.GetAsync(BaseUrl + "/nothing.js");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var post = await _http.PostAsync(BaseUrl + "/index.html", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task RefusesOtherHostNames()
    {
        await using var running = Start();
        foreach (string host in new[] { "evil.example.com", "coreloom.attacker.net", "192.168.1.24.nip.io" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/api/info");
            request.Headers.Host = host;
            using var response = await _http.SendAsync(request);
            Assert.Equal((HttpStatusCode)421, response.StatusCode);
        }

        foreach (string host in new[] { "localhost", "jerry-pc.local", "JERRY-PC", "jerry-pc.home.arpa", "[fe80::1]:47101", "192.168.1.24:47101" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/api/info");
            request.Headers.Host = host;
            using var response = await _http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task InfoSaysWhetherPairingIsOpen()
    {
        await using var running = Start();
        var closed = await GetJsonAsync("/api/info");
        Assert.Equal("Coreloom", closed.GetProperty("app").GetString());
        Assert.Equal("JERRY-PC", closed.GetProperty("name").GetString());
        Assert.Equal(_store.PcId, closed.GetProperty("pc").GetString());
        Assert.False(closed.GetProperty("pairing").GetProperty("code").GetBoolean());

        _pairing.Open();
        Assert.True((await GetJsonAsync("/api/info")).GetProperty("pairing").GetProperty("code").GetBoolean());
    }

    [Fact]
    public async Task PairsWithTheCodeThenReadsLiveReadings()
    {
        await using var running = Start();
        _pairing.Open();
        string code = _pairing.Current!.Code;
        var paired = await PairAsync("code", Encoding.ASCII.GetBytes(code), "Test Phone");
        Assert.NotNull(paired);
        Assert.Equal("JERRY-PC", paired!.Value.GetProperty("name").GetString());
        var device = Assert.Single(_store.Devices);
        Assert.Equal("Test Phone", device.Name);
        Assert.Equal("code", device.Via);
        Assert.NotEqual(code, _pairing.Current!.Code); // a used code is replaced

        var session = await OpenSessionAsync(paired.Value);
        Assert.Equal("Test Phone", session.Hello.GetProperty("device").GetString());

        // Nothing yet: "nothing new" after the long poll.
        using (var empty = await session.PostAsync("/api/live", new { after = 0 }))
        {
            Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        }

        _server.HistoryProvider = () => """{"now":1}"""u8.ToArray();
        _server.Publish("""{"t":42}"""u8.ToArray());
        Assert.True(_server.HasViewers);
        var (first, counter) = await session.LiveAsync(0);
        Assert.Equal(42, first.GetProperty("snapshot").GetProperty("t").GetInt32());
        Assert.Equal(1, first.GetProperty("history").GetProperty("now").GetInt32());
        Assert.Contains(device.Id, _server.ConnectedDevices);

        // The next reading waits for a publish, and carries no history.
        long seq = first.GetProperty("seq").GetInt64();
        var next = session.LiveAsync(seq);
        await Task.Delay(200);
        _server.Publish("""{"t":43}"""u8.ToArray());
        var (second, _) = await next;
        Assert.Equal(43, second.GetProperty("snapshot").GetProperty("t").GetInt32());
        Assert.False(second.TryGetProperty("history", out _));
        Assert.True(counter > 0);
    }

    [Fact]
    public async Task ALinkPairsOnceAndAWrongSecretIsRefused()
    {
        await using var running = Start();
        _pairing.Open();
        byte[] secret = ConnectCrypto.FromBase64Url(_pairing.Current!.LinkSecret)!;
        Assert.NotNull(await PairAsync("link", secret, "First"));
        Assert.Null(await PairAsync("link", secret, "Second"));
        Assert.Null(await PairAsync("code", "000000"u8.ToArray(), "Guess"));
        Assert.Equal("qr", Assert.Single(_store.Devices).Via);
    }

    [Fact]
    public async Task PairingIsRefusedWhileClosed()
    {
        await using var running = Start();
        using var phone = new ConnectCrypto.EphemeralKey();
        using var response = await PostJsonAsync("/api/pair/start", new { kind = "code", pub = ConnectCrypto.ToBase64Url(phone.PublicKey) });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var badKey = await PostJsonAsync("/api/pair/start", new { kind = "code", pub = "AAAA" });
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
    }

    [Fact]
    public async Task AHomeScreenLinkPairsWithoutTheSettingsPageOpen()
    {
        await using var running = Start();
        _pairing.Open();
        var paired = (await PairAsync("link", ConnectCrypto.FromBase64Url(_pairing.Current!.LinkSecret)!, "Safari"))!.Value;
        _pairing.Close();
        var session = await OpenSessionAsync(paired);

        using var response = await session.PostAsync("/api/setup-link", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        byte[] plain = ConnectCrypto.Open(session.Encrypt, await response.Content.ReadAsByteArrayAsync(), $"coreloom/setup\n{session.Counter}")!;
        using var setup = JsonDocument.Parse(plain);
        byte[] secret = ConnectCrypto.FromBase64Url(setup.RootElement.GetProperty("secret").GetString())!;

        Assert.NotNull(await PairAsync("link", secret, "Home Screen"));
        Assert.Equal("home", _store.Devices.Single(d => d.Name == "Home Screen").Via);
        Assert.Null(await PairAsync("link", secret, "Again"));
    }

    [Fact]
    public async Task ReplayedForgedAndUnpairedRequestsAreRefused()
    {
        await using var running = Start();
        _pairing.Open();
        var paired = (await PairAsync("code", Encoding.ASCII.GetBytes(_pairing.Current!.Code), "Phone"))!.Value;
        var session = await OpenSessionAsync(paired);
        _server.Publish("""{"t":1}"""u8.ToArray());
        await session.LiveAsync(0);

        // The same counter again.
        session.Counter--;
        using (var replay = await session.PostAsync("/api/live", new { after = 0 }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Contains("replay", await replay.Content.ReadAsStringAsync());
        }

        // A request signed with another key.
        string body = JsonSerializer.Serialize(new { session = session.Id, n = session.Counter + 10, after = 0 });
        using (var forged = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/live") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
        {
            forged.Headers.Add("X-Coreloom-Auth", ConnectCrypto.ToBase64Url(ConnectCrypto.Mac(ConnectCrypto.RandomBytes(32), "/api/live\n" + body)));
            using var response = await _http.SendAsync(forged);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("auth", await response.Content.ReadAsStringAsync());
        }

        // An unknown device can't open a session.
        using (var unknown = await PostJsonAsync("/api/session", new { device = "nobody", nonce = ConnectCrypto.ToBase64Url(ConnectCrypto.RandomBytes(16)) }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        }

        // Forgetting this computer unpairs the phone, and its session ends.
        using (var forget = await session.PostAsync("/api/forget", new { }))
        {
            Assert.Equal(HttpStatusCode.NoContent, forget.StatusCode);
        }

        Assert.Empty(_store.Devices);
        using (var after = await session.PostAsync("/api/live", new { after = 0 }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        }
    }

    // ---- The phone's side -------------------------------------------------------------------------------------------

    private async Task<JsonElement?> PairAsync(string kind, byte[] secret, string name)
    {
        using var phone = new ConnectCrypto.EphemeralKey();
        using var start = await PostJsonAsync("/api/pair/start", new { kind, pub = ConnectCrypto.ToBase64Url(phone.PublicKey) });
        if (start.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        using var startJson = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        byte[] pcPublic = ConnectCrypto.FromBase64Url(startJson.RootElement.GetProperty("pub").GetString())!;
        byte[] shared = phone.SharedSecret(pcPublic)!;
        var (encrypt, mac) = ConnectCrypto.PairingKeys(shared, secret, phone.PublicKey, pcPublic);
        using var finish = await PostJsonAsync("/api/pair/finish", new
        {
            attempt = startJson.RootElement.GetProperty("attempt").GetString(),
            proof = ConnectCrypto.ToBase64Url(ConnectCrypto.Mac(mac, "coreloom/pair/phone")),
            device = ConnectCrypto.ToBase64Url(ConnectCrypto.Seal(encrypt, JsonSerializer.SerializeToUtf8Bytes(new { name }), "coreloom/pair/device")),
        });
        if (finish.StatusCode != HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.Forbidden, finish.StatusCode);
            return null;
        }

        using var finishJson = JsonDocument.Parse(await finish.Content.ReadAsStringAsync());
        byte[] result = ConnectCrypto.Open(encrypt, ConnectCrypto.FromBase64Url(finishJson.RootElement.GetProperty("result").GetString())!, "coreloom/pair/result")!;
        return JsonDocument.Parse(result).RootElement.Clone();
    }

    private async Task<PhoneSession> OpenSessionAsync(JsonElement paired)
    {
        byte[] phoneNonce = ConnectCrypto.RandomBytes(16);
        using var response = await PostJsonAsync("/api/session", new { device = paired.GetProperty("device").GetString(), nonce = ConnectCrypto.ToBase64Url(phoneNonce) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        byte[] deviceKey = ConnectCrypto.FromBase64Url(paired.GetProperty("key").GetString())!;
        var (encrypt, mac) = ConnectCrypto.SessionKeys(deviceKey, phoneNonce, ConnectCrypto.FromBase64Url(json.RootElement.GetProperty("nonce").GetString())!);
        byte[] hello = ConnectCrypto.Open(encrypt, ConnectCrypto.FromBase64Url(json.RootElement.GetProperty("hello").GetString())!, "coreloom/session/hello")!;
        return new PhoneSession(this, json.RootElement.GetProperty("session").GetString()!, encrypt, mac, JsonDocument.Parse(hello).RootElement.Clone());
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await _http.GetAsync(BaseUrl + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private Task<HttpResponseMessage> PostJsonAsync(string path, object body) =>
        _http.PostAsync(BaseUrl + path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    private sealed class PhoneSession(ConnectServerTests owner, string id, byte[] encrypt, byte[] mac, JsonElement hello)
    {
        public string Id => id;

        public byte[] Encrypt => encrypt;

        public JsonElement Hello => hello;

        public long Counter { get; set; }

        public async Task<HttpResponseMessage> PostAsync(string path, object extra)
        {
            Counter++;
            var fields = JsonSerializer.SerializeToElement(extra).EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);
            fields["session"] = id;
            fields["n"] = Counter;
            string body = JsonSerializer.Serialize(fields);
            var request = new HttpRequestMessage(HttpMethod.Post, owner.BaseUrl + path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Coreloom-Auth", ConnectCrypto.ToBase64Url(ConnectCrypto.Mac(mac, path + "\n" + body)));
            return await owner._http.SendAsync(request);
        }

        public async Task<(JsonElement Reading, long Counter)> LiveAsync(long after)
        {
            using var response = await PostAsync("/api/live", new { after });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            byte[] plain = ConnectCrypto.Open(encrypt, await response.Content.ReadAsByteArrayAsync(), $"coreloom/live\n{Counter}")!;
            Assert.NotNull(plain);
            return (JsonDocument.Parse(plain).RootElement.Clone(), Counter);
        }
    }
}
