using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.UnitTests.Domain.Products;

/// <summary>Tests del value object ProductName.</summary>
public class ProductNameTests
{
    [Fact]
    public void Create_ConNombreValido_CreaElValueObject()
    {
        Assert.Equal("Teclado mecánico", ProductName.Create("Teclado mecánico").Value);
    }

    [Fact]
    public void Create_QuitaLosEspaciosDeLosExtremos()
    {
        Assert.Equal("Teclado", ProductName.Create("   Teclado   ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_SinNombre_LanzaDomainException(string? value)
    {
        Assert.Equal("ProductName.Required", Assert.Throws<DomainException>(() => ProductName.Create(value)).Code);
    }

    [Fact]
    public void Create_ConNombreDemasiadoCorto_LanzaDomainException()
    {
        // Los bugs viven en los bordes: probamos justo por debajo del mínimo.
        var tooShort = new string('a', ProductName.MinLength - 1);

        Assert.Equal("ProductName.InvalidLength", Assert.Throws<DomainException>(() => ProductName.Create(tooShort)).Code);
    }

    [Fact]
    public void Create_ConNombreDemasiadoLargo_LanzaDomainException()
    {
        var tooLong = new string('a', ProductName.MaxLength + 1);

        Assert.Equal("ProductName.InvalidLength", Assert.Throws<DomainException>(() => ProductName.Create(tooLong)).Code);
    }

    [Fact]
    public void Create_EnLosLimitesExactos_Funciona()
    {
        Assert.Equal(ProductName.MinLength, ProductName.Create(new string('a', ProductName.MinLength)).Value.Length);
        Assert.Equal(ProductName.MaxLength, ProductName.Create(new string('a', ProductName.MaxLength)).Value.Length);
    }
}
