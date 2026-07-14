using System.Collections.Generic;
using System.Reflection;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.ViewModels;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationMainViewModelReprocessTests
{
    private static ClassificationMainViewModel NewVm(bool hasBatchRun)
    {
        var vm = new ClassificationMainViewModel();
        vm.BackendUrl = "http://localhost:7071";
        SetPrivateField(vm, "_hasBatchRun", hasBatchRun);
        return vm;
    }

    [Fact]
    public void ResolveReprocessCandidates_SinMarcas_DevuelveTodosLosElegibles()
    {
        var vm = NewVm(hasBatchRun: true);
        vm.Files.Add(new ClassificationDocumentItem { FileName = "err.pdf", Status = "Error" });
        vm.Files.Add(new ClassificationDocumentItem { FileName = "ok.pdf", Status = "OK" });
        vm.Files.Add(new ClassificationDocumentItem { FileName = "pend.pdf", Status = "Pendiente" });

        var candidatos = InvokeResolve(vm);

        Assert.Equal(2, candidatos.Count);
        Assert.Contains(candidatos, f => f.FileName == "err.pdf");
        Assert.Contains(candidatos, f => f.FileName == "pend.pdf");
    }

    [Fact]
    public void ResolveReprocessCandidates_ConMarcas_SoloMarcadosElegibles()
    {
        var vm = NewVm(hasBatchRun: true);
        var err = new ClassificationDocumentItem { FileName = "err.pdf", Status = "Error", IsSelected = true };
        var ok = new ClassificationDocumentItem { FileName = "ok.pdf", Status = "OK", IsSelected = true };
        var errNoMarcado = new ClassificationDocumentItem { FileName = "err2.pdf", Status = "Error", IsSelected = false };
        vm.Files.Add(err);
        vm.Files.Add(ok);
        vm.Files.Add(errNoMarcado);

        var candidatos = InvokeResolve(vm);

        Assert.Single(candidatos);
        Assert.Equal("err.pdf", candidatos[0].FileName);
    }

    [Fact]
    public void CanReprocess_SoloPendientes_AntesDelPrimerLote_EsFalse()
    {
        var vm = NewVm(hasBatchRun: false);
        vm.Files.Add(new ClassificationDocumentItem { FileName = "pend.pdf", Status = "Pendiente" });

        Assert.False(InvokeCanReprocess(vm));
    }

    [Fact]
    public void CanReprocess_ConError_EsTrue_AunSinLotePrevio()
    {
        var vm = NewVm(hasBatchRun: false);
        vm.Files.Add(new ClassificationDocumentItem { FileName = "err.pdf", Status = "Error" });

        Assert.True(InvokeCanReprocess(vm));
    }

    private static IReadOnlyList<ClassificationDocumentItem> InvokeResolve(ClassificationMainViewModel vm)
    {
        var method = typeof(ClassificationMainViewModel).GetMethod(
            "ResolveReprocessCandidates",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (IReadOnlyList<ClassificationDocumentItem>)method!.Invoke(vm, null)!;
    }

    private static bool InvokeCanReprocess(ClassificationMainViewModel vm)
    {
        var method = typeof(ClassificationMainViewModel).GetMethod(
            "CanReprocess",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (bool)method!.Invoke(vm, null)!;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
