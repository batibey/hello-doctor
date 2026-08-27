using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HelloDoctor.Api.Services;

public class EmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IOptions<EmailOptions> options, ILogger<EmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendPasswordResetAsync(string toAddress, string toName, string rawToken,
        CancellationToken ct = default)
    {
        var link = $"{_options.AppBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        // SMTP tanımsızsa akışı kırmıyoruz; bağlantı loglanır ki yerelde
        // e-posta altyapısı kurmadan da sıfırlama denenebilsin.
        if (!_options.IsConfigured)
        {
            _logger.LogWarning(
                "Smtp:Host tanımsız, e-posta gönderilmedi. {Email} için sıfırlama bağlantısı: {Link}",
                toAddress, link);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = "HelloDoctor · Şifre sıfırlama";
        message.Body = new BodyBuilder
        {
            TextBody =
                $"Merhaba {toName},\n\n" +
                "Şifrenizi sıfırlamak için aşağıdaki bağlantıya gidin. Bağlantı 1 saat geçerlidir " +
                "ve yalnızca bir kez kullanılabilir.\n\n" +
                $"{link}\n\n" +
                "Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz; şifreniz değişmez.\n\n" +
                "ÖNEMLİ: Mesajlarınız uçtan uca şifrelidir ve anahtarınız şifrenizden türetilir. " +
                "Şifrenizi sıfırladığınızda yeni bir anahtar oluşturulur, bu yüzden sıfırlamadan " +
                "önceki mesajlarınız okunamaz hale gelir.\n\n" +
                "HelloDoctor",
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

        if (!string.IsNullOrWhiteSpace(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password ?? "", ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Şifre sıfırlama e-postası gönderildi: {Email}", toAddress);
    }
}
