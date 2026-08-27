using HelloDoctor.Api.Models;

namespace HelloDoctor.Api.Data;

public static class Mapping
{
    // PublicKey bilerek dışarı veriliyor: karşı tarafa mesaj şifrelemek için
    // gereken tek anahtar odur. Özel anahtar malzemesi buraya girmez.
    public static UserDto ToDto(this User u) => new(
        u.Id, u.Email, u.FullName, u.Role.ToString(), u.AvatarColor,
        u.Specialty, u.Title, u.Rating, u.ExperienceYears, u.Bio, u.Age, u.BloodType,
        u.PublicKey);

    public static KeyBundle ToKeyBundle(this User u) =>
        new(u.PublicKey, u.WrappedPrivateKey, u.KeyWrapSalt, u.KeyWrapIv);
}
