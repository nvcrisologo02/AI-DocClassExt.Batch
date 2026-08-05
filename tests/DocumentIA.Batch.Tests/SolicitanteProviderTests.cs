using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class SolicitanteProviderTests
{
    private const string Programa = "DocumentIA.Batch";
    private static readonly TimeSpan TimeoutHolgado = TimeSpan.FromSeconds(5);

    private sealed class IdentidadFalsa : IIdentidadWindows
    {
        public string? Upn { get; init; }
        public string? Cuenta { get; init; }
        public string? Nombre { get; init; }
        public TimeSpan RetardoUpn { get; init; } = TimeSpan.Zero;
        public Exception? ExcepcionUpn { get; init; }

        public string? ObtenerUpn()
        {
            if (RetardoUpn > TimeSpan.Zero)
            {
                Thread.Sleep(RetardoUpn);
            }

            if (ExcepcionUpn is not null)
            {
                throw ExcepcionUpn;
            }

            return Upn;
        }

        public string? ObtenerCuentaWindows() => Cuenta;

        public string? ObtenerNombreUsuario() => Nombre;
    }

    [Fact]
    public void ResolverUsuario_ConUpnDisponible_DevuelveElUpn()
    {
        var identidad = new IdentidadFalsa
        {
            Upn = "nombre.apellido@sareb.es",
            Cuenta = @"SAREB\usuario",
            Nombre = "usuario"
        };

        Assert.Equal("nombre.apellido@sareb.es", SolicitanteProvider.ResolverUsuario(identidad, TimeoutHolgado));
    }

    [Fact]
    public void ResolverUsuario_SinUpn_CaeALaCuentaWindows()
    {
        var identidad = new IdentidadFalsa { Upn = null, Cuenta = @"SAREB\usuario", Nombre = "usuario" };

        Assert.Equal(@"SAREB\usuario", SolicitanteProvider.ResolverUsuario(identidad, TimeoutHolgado));
    }

    [Fact]
    public void ResolverUsuario_SinUpnNiCuenta_CaeAlNombreDeSesion()
    {
        var identidad = new IdentidadFalsa { Upn = "   ", Cuenta = string.Empty, Nombre = "usuario" };

        Assert.Equal("usuario", SolicitanteProvider.ResolverUsuario(identidad, TimeoutHolgado));
    }

    [Fact]
    public void ResolverUsuario_SinNingunDato_DevuelveNull()
    {
        var identidad = new IdentidadFalsa();

        Assert.Null(SolicitanteProvider.ResolverUsuario(identidad, TimeoutHolgado));
    }

    [Fact]
    public void ResolverUsuario_SiElUpnExcedeElTimeout_NoBloqueaYSigueLaCascada()
    {
        var identidad = new IdentidadFalsa
        {
            Upn = "nombre.apellido@sareb.es",
            RetardoUpn = TimeSpan.FromSeconds(5),
            Cuenta = @"SAREB\usuario"
        };

        var resuelto = SolicitanteProvider.ResolverUsuario(identidad, TimeSpan.FromMilliseconds(50));

        Assert.Equal(@"SAREB\usuario", resuelto);
    }

    [Fact]
    public void ResolverUsuario_SiElUpnRevienta_SigueLaCascada()
    {
        var identidad = new IdentidadFalsa
        {
            ExcepcionUpn = new InvalidOperationException("fallo al resolver"),
            Cuenta = @"SAREB\usuario"
        };

        Assert.Equal(@"SAREB\usuario", SolicitanteProvider.ResolverUsuario(identidad, TimeoutHolgado));
    }

    [Fact]
    public void Componer_ConUsuario_UneProgramaYUsuario()
    {
        Assert.Equal(
            "DocumentIA.Batch/nombre.apellido@sareb.es",
            SolicitanteProvider.Componer(Programa, "nombre.apellido@sareb.es"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Componer_SinUsuario_DevuelveSoloElPrograma(string? usuario)
    {
        Assert.Equal(Programa, SolicitanteProvider.Componer(Programa, usuario));
    }

    [Fact]
    public void Componer_RecortaEspaciosSobrantes()
    {
        Assert.Equal(
            "DocumentIA.Batch/nombre.apellido@sareb.es",
            SolicitanteProvider.Componer("  DocumentIA.Batch  ", "  nombre.apellido@sareb.es  "));
    }

    [Fact]
    public void Componer_CuandoExcedeElLimite_RecortaElUsuarioYConservaElPrograma()
    {
        var usuarioLargo = new string('u', 300);

        var resultado = SolicitanteProvider.Componer(Programa, usuarioLargo);

        Assert.Equal(SolicitanteProvider.LongitudMaxima, resultado.Length);
        Assert.StartsWith($"{Programa}/", resultado);
    }

    [Fact]
    public void Componer_CuandoElProgramaAgotaElLimite_DevuelveSoloElPrograma()
    {
        var programaLargo = new string('p', SolicitanteProvider.LongitudMaxima - 1);

        var resultado = SolicitanteProvider.Componer(programaLargo, "nombre.apellido@sareb.es");

        Assert.Equal(programaLargo, resultado);
    }

    [Fact]
    public void ObtenerSolicitante_SiempreEmpiezaPorElPrograma()
    {
        var resultado = SolicitanteProvider.ObtenerSolicitante(Programa);

        Assert.StartsWith(Programa, resultado);
        Assert.True(resultado.Length <= SolicitanteProvider.LongitudMaxima);
    }
}
