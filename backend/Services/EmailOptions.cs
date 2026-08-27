namespace HelloDoctor.Api.Services;

public class EmailOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@hellodoctor.local";
    public string FromName { get; set; } = "HelloDoctor";

    // STARTTLS varsayılan. Yerel yakalayıcılarda (Mailpit) TLS yoktur.
    public bool UseStartTls { get; set; } = true;

    // Sıfırlama bağlantısının işaret ettiği ön yüz adresi.
    public string AppBaseUrl { get; set; } = "http://localhost:5173";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
