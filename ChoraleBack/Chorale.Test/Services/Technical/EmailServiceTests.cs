using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using ChoraleBackEnd.Services.Technical;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Technical;

[TestFixture]
public sealed class EmailServiceTests
{
    private const string Address = "expediteur@chorale.local";

    [Test]
    public void BuildFromAddress_WithFromName_CarriesAddressAndDisplayName()
    {
        var result = EmailService.BuildFromAddress(Address, "Chorale Helper");

        Assert.That(result.Address, Is.EqualTo(Address));
        Assert.That(result.DisplayName, Is.EqualTo("Chorale Helper"));
    }

    [Test]
    public void BuildFromAddress_WithoutFromName_FallsBackToAddressOnly()
    {
        var result = EmailService.BuildFromAddress(Address, null);

        Assert.That(result.Address, Is.EqualTo(Address));
        Assert.That(result.DisplayName, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void BuildFromAddress_FromNameEmptyOrBlank_TreatedAsAbsent(string fromName)
    {
        var result = EmailService.BuildFromAddress(Address, fromName);

        Assert.That(result.Address, Is.EqualTo(Address));
        Assert.That(result.DisplayName, Is.Empty);
    }

    [Test]
    public void BuildFromAddress_WithAccents_PreservesExactName()
    {
        const string accentedName = "Chœur Sainte-Cécile";

        var result = EmailService.BuildFromAddress(Address, accentedName);

        Assert.That(result.DisplayName, Is.EqualTo(accentedName));
    }

    [Test]
    public void BuildFromAddress_WithFromName_EncodesAsUtf8()
    {
        var result = EmailService.BuildFromAddress(Address, "Chœur Sainte-Cécile");

        var encodingField = typeof(MailAddress)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(Encoding));

        Assert.That(encodingField.GetValue(result), Is.EqualTo(Encoding.UTF8));
    }
}
