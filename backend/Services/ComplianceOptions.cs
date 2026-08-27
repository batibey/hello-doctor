namespace HelloDoctor.Api.Services;

public class ComplianceOptions
{
    public const string SectionName = "Compliance";

    // Aydınlatma metni ve açık rıza sürümleri. Metin değişince sürüm artırılır;
    // eski sürümü kabul etmiş kullanıcıdan yeniden onay istenir.
    public string PrivacyNoticeVersion { get; set; } = "1.0";
    public string HealthDataConsentVersion { get; set; } = "1.0";

    // Saklama süreleri (gün). Sağlık mevzuatının öngördüğü asgari süreler
    // netleşince buradan ayarlanır — kod değişikliği gerekmez.
    //
    // 0 = budama yapma. Varsayılan olarak yalnızca teknik kayıtlar budanıyor;
    // mesaj ve randevu gibi tıbbi kayıtlara Bakanlık görüşü alınmadan
    // dokunulmuyor, çünkü erken silmek de mevzuata aykırı olabilir.
    public int AccessLogRetentionDays { get; set; } = 730;      // 2 yıl
    public int UsedResetTokenRetentionDays { get; set; } = 30;
    public int MessageRetentionDays { get; set; }               // 0: budama yok
    public int AppointmentRetentionDays { get; set; }           // 0: budama yok

    // Acil durum uyarısında gösterilecek numara.
    public string EmergencyNumber { get; set; } = "112";
}
