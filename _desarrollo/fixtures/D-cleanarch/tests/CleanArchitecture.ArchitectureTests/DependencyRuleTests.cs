using System.Reflection;
using CleanArchitecture.Application.Products.CreateProduct;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.ArchitectureTests;

/// <summary>La Dependency Rule, verificada con reflexión pura (nivel 1).</summary>
public class DependencyRuleTests
{
    // 📘 docs/02-clean-architecture.md
    //
    // ⭐ POR QUÉ EXISTE ESTE ARCHIVO: una regla que solo está escrita en un
    // README se rompe el día que alguien con prisa agrega un "using". Estos
    // tests convierten la regla en algo que FALLA, con un mensaje claro, antes
    // de llegar a la rama principal.
    //
    // EXPERIMENTO (hazlo, son 30 segundos): abre Money.cs y agrega "using
    // Dapper;" al inicio. No compilará, porque Domain ni siquiera tiene el
    // paquete. Ahora prueba al revés: en un handler de Application, agrega
    // "using CleanArchitecture.Infrastructure.Persistence;". Compila... pero
    // este test se pone rojo y te dice exactamente por qué.
    //
    // Este primer nivel usa solo reflexión: sin librerías, para que veas que la
    // idea no tiene magia. El nivel 2 (LayerDependencyTests) usa ArchUnitNET,
    // que permite reglas mucho más finas con menos código.

    private static readonly Assembly Domain = typeof(Product).Assembly;
    private static readonly Assembly Application = typeof(CreateProductHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static IReadOnlyList<string> ReferencesOf(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name!)];

    [Fact]
    public void Domain_NoDependeDeNingunaOtraCapa()
    {
        var references = ReferencesOf(Domain);

        Assert.DoesNotContain(references, name => name.StartsWith("CleanArchitecture.", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_NoDependeDeNingunaTecnologiaExterna()
    {
        // El corazón del sistema no sabe que existen las bases de datos, la web
        // ni ninguna librería de terceros. Por eso es estable y se puede probar
        // sin montar nada.
        var references = ReferencesOf(Domain);

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Dapper", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Data", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_SoloDependeDeDomain()
    {
        var references = ReferencesOf(Application);

        Assert.Contains("CleanArchitecture.Domain", references);
        Assert.DoesNotContain("CleanArchitecture.Infrastructure", references);
        Assert.DoesNotContain("CleanArchitecture.Api", references);
    }

    [Fact]
    public void Application_NoConoceLaBaseDeDatosNiLaWeb()
    {
        // Application puede usar la ABSTRACCIÓN del contenedor de dependencias
        // (Microsoft.Extensions.DependencyInjection.Abstractions) para
        // registrarse, pero nada de Dapper, SQLite ni ASP.NET Core.
        var references = ReferencesOf(Application);

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Dapper", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Data", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_NoDependeDeLaApi()
    {
        // Las flechas apuntan hacia dentro: la capa que habla con la base de
        // datos no sabe que existe una API encima.
        Assert.DoesNotContain("CleanArchitecture.Api", ReferencesOf(Infrastructure));
    }

    [Fact]
    public void Api_EsLaUnicaQueConoceTodasLasCapas()
    {
        // El composition root es el único lugar donde se ensambla todo.
        var references = ReferencesOf(Api);

        Assert.Contains("CleanArchitecture.Application", references);
        Assert.Contains("CleanArchitecture.Infrastructure", references);
    }

    [Fact]
    public void Infrastructure_SoloExponeSuRegistroDeDependencias()
    {
        // Todos los adaptadores son internal: así ningún endpoint puede saltarse
        // el puerto y usar el repositorio concreto, ni siquiera por accidente.
        var publicTypes = Infrastructure.GetExportedTypes().Select(type => type.Name);

        Assert.Equal(["DependencyInjection"], publicTypes);
    }

    [Fact]
    public void LasEntidades_NoTienenPropiedadesModificablesDesdeFuera()
    {
        // La regla que convierte un "modelo anémico" en un modelo rico: el
        // estado solo cambia por métodos de negocio, nunca asignando propiedades.
        var entityTypes = Domain.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && InheritsFromEntity(type));

        foreach (var type in entityTypes)
        {
            var settableProperties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{type.Name}.{property.Name}");

            Assert.Empty(settableProperties);
        }
    }

    private static bool InheritsFromEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().Name.StartsWith("Entity", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
