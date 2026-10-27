using System.Security.Cryptography;

namespace Coreloom.Core.Connect;

/// <summary>How a phone proves it may pair: the link in the QR code (or a Home Screen link) or the six-digit code.</summary>
public enum PairingKind
{
    Link,
    Code,
}

/// <summary>What Settings › Connect shows while pairing is open.</summary>
public sealed record PairingTicket(string LinkSecret, string Code, DateTimeOffset Expires);

/// <summary>
/// Pairing is open only while Settings › Connect is on screen (like a Bluetooth pairing mode). The QR code's link
/// carries 32 random bytes and the code is six random digits; both are renewed every five minutes and after each
/// pairing, and the code after five wrong tries. Already paired phones can ask for a one-time link (valid fifteen
/// minutes) that a Home Screen copy of the app pairs itself with, whether or not the page is open.
/// </summary>
public sealed class PairingWindow
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SetupLinkLifetime = TimeSpan.FromMinutes(15);
    public const int CodeAttempts = 5;
    public const int MaximumSetupLinks = 4;

    /// <summary>Wrong guesses (codes or links) allowed per minute before pairing pauses for a minute.</summary>
    public const int FailuresPerMinute = 10;

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly List<(byte[] Secret, DateTimeOffset Expires)> _setupLinks = [];
    private readonly Queue<DateTimeOffset> _failures = new();
    private PairingTicket? _ticket;
    private byte[]? _linkSecret;
    private int _attemptsLeft;
    private DateTimeOffset _pausedUntil;

    public PairingWindow(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Raised when the link and code change (opened, closed, renewed, used).</summary>
    public event Action? Changed;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _ticket is not null;
            }
        }
    }

    /// <summary>The current link secret and code (renewed first if they've expired), or null while closed.</summary>
    public PairingTicket? Current
    {
        get
        {
            bool renewed;
            PairingTicket? ticket;
            lock (_gate)
            {
                renewed = _ticket is not null && _now() >= _ticket.Expires && RenewLocked();
                ticket = _ticket;
            }

            if (renewed)
            {
                Changed?.Invoke();
            }

            return ticket;
        }
    }

    public void Open()
    {
        lock (_gate)
        {
            if (_ticket is not null)
            {
                return;
            }

            RenewLocked();
        }

        Changed?.Invoke();
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_ticket is null)
            {
                return;
            }

            _ticket = null;
            _linkSecret = null;
        }

        Changed?.Invoke();
    }

    /// <summary>A new link and code now (the page's refresh button).</summary>
    public void Renew()
    {
        lock (_gate)
        {
            if (_ticket is null)
            {
                return;
            }

            RenewLocked();
        }

        Changed?.Invoke();
    }

    /// <summary>A one-time link secret for a Home Screen copy of the app (base64url), or null when too many are out.</summary>
    public string? MintSetupLink()
    {
        lock (_gate)
        {
            var now = _now();
            _setupLinks.RemoveAll(l => l.Expires <= now);
            if (_setupLinks.Count >= MaximumSetupLinks)
            {
                _setupLinks.RemoveAt(0);
            }

            byte[] secret = ConnectCrypto.RandomBytes(32);
            _setupLinks.Add((secret, now + SetupLinkLifetime));
            return ConnectCrypto.ToBase64Url(secret);
        }
    }

    /// <summary>True while wrong guesses have paused pairing.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _now() < _pausedUntil;
            }
        }
    }

    /// <summary>The secrets a phone's proof is checked against: the current link or code, and the Home Screen links.</summary>
    internal IReadOnlyList<byte[]> Candidates(PairingKind kind)
    {
        lock (_gate)
        {
            var now = _now();
            if (now < _pausedUntil)
            {
                return [];
            }

            if (_ticket is not null && now >= _ticket.Expires)
            {
                RenewLocked();
            }

            var result = new List<byte[]>();
            if (kind == PairingKind.Code)
            {
                if (_ticket is not null)
                {
                    result.Add(System.Text.Encoding.ASCII.GetBytes(_ticket.Code));
                }

                return result;
            }

            if (_linkSecret is not null)
            {
                result.Add(_linkSecret);
            }

            _setupLinks.RemoveAll(l => l.Expires <= now);
            result.AddRange(_setupLinks.Select(l => l.Secret));
            return result;
        }
    }

    /// <summary>Whether a pairing of this kind could succeed now (the phone is told to open Settings › Connect if not).</summary>
    internal bool Accepts(PairingKind kind) => Candidates(kind).Count > 0;

    /// <summary>True for a Home Screen link (not the QR code's).</summary>
    internal bool IsSetupLink(byte[] secret)
    {
        lock (_gate)
        {
            return _setupLinks.Any(l => CryptographicOperations.FixedTimeEquals(l.Secret, secret));
        }
    }

    /// <summary>A phone paired with this secret: it can't be used again.</summary>
    internal void Used(byte[] secret)
    {
        bool changed = false;
        lock (_gate)
        {
            if (_setupLinks.RemoveAll(l => CryptographicOperations.FixedTimeEquals(l.Secret, secret)) == 0 && _ticket is not null)
            {
                changed = RenewLocked();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>A wrong code or link: the code is renewed after five, and pairing pauses after ten in a minute.</summary>
    internal void Failed(PairingKind kind)
    {
        bool changed = false;
        lock (_gate)
        {
            var now = _now();
            _failures.Enqueue(now);
            while (_failures.Count > 0 && now - _failures.Peek() > TimeSpan.FromMinutes(1))
            {
                _failures.Dequeue();
            }

            if (_failures.Count >= FailuresPerMinute)
            {
                _pausedUntil = now + TimeSpan.FromMinutes(1);
                _failures.Clear();
            }

            if (kind == PairingKind.Code && _ticket is not null && --_attemptsLeft <= 0)
            {
                changed = RenewLocked();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private bool RenewLocked()
    {
        _linkSecret = ConnectCrypto.RandomBytes(32);
        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        _ticket = new PairingTicket(ConnectCrypto.ToBase64Url(_linkSecret), code, _now() + Lifetime);
        _attemptsLeft = CodeAttempts;
        return true;
    }
}
