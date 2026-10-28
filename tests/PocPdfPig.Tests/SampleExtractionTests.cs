using System.Text.Json.Nodes;
using PocPdfPig.Api.Models;
using PocPdfPig.Api.Services;

namespace PocPdfPig.Tests;

/// <summary>
/// Regressão: executa cada template pronto contra o PDF de amostra correspondente e confere valores-chave.
/// Se alterar o motor ou um template, rode `dotnet test` para garantir que nada quebrou.
/// </summary>
public class SampleExtractionTests
{
    private static readonly string Root = FindRoot();
    private readonly PdfExtractionService _service = new();

    private ExtractionResult Run(string sample, string template)
    {
        var json = File.ReadAllText(Path.Combine(Root, "src", "PocPdfPig.Api", "Templates", template + ".json"));
        var (parsed, errors) = TemplateParser.Parse(json);
        Assert.True(parsed is not null, string.Join("\n", errors));

        var pdf = File.ReadAllBytes(Path.Combine(Root, "pdf-amostras", sample));
        var result = _service.Extract(pdf, parsed!, sample, debug: false);

        Assert.True(result.Success, string.Join("\n", result.Warnings));
        Assert.Empty(result.Warnings);
        return result;
    }

    private static string? S(JsonNode? node, string path) => Get(node, path)?.GetValue<object>().ToString();
    private static decimal D(JsonNode? node, string path) => Get(node, path)!.GetValue<decimal>();

    /// <summary>Navega com caminhos como "debitos[1].valor".</summary>
    private static JsonNode? Get(JsonNode? node, string path)
    {
        foreach (var part in path.Split('.'))
        {
            var name = part;
            int? index = null;
            var bracket = part.IndexOf('[');
            if (bracket >= 0)
            {
                name = part[..bracket];
                index = int.Parse(part[(bracket + 1)..^1]);
            }
            if (name.Length > 0) node = node?[name];
            if (index is not null) node = node?[index.Value];
        }
        return node;
    }

    [SampleFact]
    public void DarfIrpj()
    {
        var data = Run("darf-irpj.pdf", "darf-comprovante-arrecadacao").Data;
        Assert.Equal(4, data["comprovantes"]!.AsArray().Count);
        Assert.Equal("34.335.332/0001-49", S(data, "comprovantes[0].cnpj"));
        Assert.Equal("K2I INTERMEDIACAO LTDA.", S(data, "comprovantes[0].razaoSocial"));
        Assert.Equal("07012511825430641", S(data, "comprovantes[0].numeroDocumento"));
        Assert.Equal(360492.27m, D(data, "comprovantes[0].composicao[0].principal"));
        Assert.Equal("2025-04-29", S(data, "comprovantes[0].dataArrecadacao"));
        Assert.Equal(4288.01m, D(data, "comprovantes[3].totais.multa"));
        Assert.Equal(26452.73m, D(data, "comprovantes[3].composicao[0].total"));
    }

    [SampleFact]
    public void DarfCsll()
    {
        var data = Run("darf-csll.pdf", "darf-comprovante-arrecadacao").Data;
        Assert.Equal(5, data["comprovantes"]!.AsArray().Count);
        Assert.Equal("K2I INTERMEDIACAO LTDA.", S(data, "comprovantes[0].razaoSocial"));
        Assert.Equal("2372", S(data, "comprovantes[0].composicao[0].codigo"));
        Assert.Equal("2026-01-30", S(data, "comprovantes[0].dataArrecadacao"));
        Assert.Equal("341 - BANCO ITAU S A", S(data, "comprovantes[0].banco"));
        Assert.Equal(777.10m, D(data, "comprovantes[1].composicao[0].juros"));
    }

    [SampleFact]
    public void DarfRelacaoPagamentos()
    {
        var data = Run("darf-pagamentos.pdf", "darf-relacao-pagamentos").Data;
        var pagamentos = data["pagamentos"]!.AsArray();
        Assert.Equal(1000, pagamentos.Count);
        Assert.Equal("61.149.589/0001-89", S(data, "cnpjContribuinte"));
        Assert.Equal("07162626068114993", S(data, "pagamentos[0].numeroDocumento"));
        Assert.Equal(6032.88m, D(data, "pagamentos[14].multa"));
        Assert.Equal("3206174774", S(data, "pagamentos[999].numeroDocumento"));
        Assert.Equal(12657864.81m, D(data, "pagamentos[999].total"));
    }

    [SampleFact]
    public void DctfWeb()
    {
        var data = Run("dctfweb.pdf", "dctfweb").Data;
        Assert.Equal("KANGU PARTICIPACOES S.A.", S(data, "nomeContribuinte"));
        Assert.Equal("32.608.999/0001-42", S(data, "cnpj"));
        Assert.Equal("2025-03-18T10:48:02", S(data, "dataHoraTransmissao"));
        Assert.Equal("OSASCO", S(data, "endereco.municipio"));
        Assert.Equal(2, data["debitos"]!.AsArray().Count);
        Assert.Equal(297.77m, D(data, "debitos[1].saldoAPagar"));
    }

    [SampleFact]
    public void DctfMensal()
    {
        var data = Run("dctf.pdf", "dctf-mensal").Data;
        Assert.Equal("2019-01", S(data, "mesAno"));
        Assert.Equal("True", S(data, "retificadora"));
        Assert.Equal("ROYAL FIC DISTRIBUIDORA DE DERIVADOS DE PETROLEO S.A.", S(data, "nomeEmpresarial"));

        var debitos = data["debitos"]!.AsArray();
        Assert.Equal(8, debitos.Count);
        Assert.Equal(26, debitos[0]!["darfs"]!.AsArray().Count);
        Assert.Equal(133179.95m, debitos[0]!["darfs"]!.AsArray().Sum(d => d!["valorPago"]!.GetValue<decimal>()));
        Assert.Equal("IRRF - Rendimentos Acumulados - Art. 12-A da Lei nº 7.713, de 1988", S(data, "debitos[2].denominacao"));
        Assert.Equal("02085.92303.240223.1.3.57-0186", S(data, "debitos[5].numeroDcomp"));
    }

    [SampleFact]
    public void NfseGuarulhos()
    {
        var data = Run("nota_fiscal_servico_guarulhos_sp.pdf", "nfse-guarulhos").Data;
        Assert.Equal("38879", S(data, "numeroNfse"));
        Assert.Equal("YLYQY9NWS", S(data, "codigoVerificacao"));
        Assert.Equal("05.075.152/0001-77", S(data, "prestador.cnpjCpf"));
        Assert.Equal("23.643.315/0001-52", S(data, "tomador.cnpjCpf"));
        Assert.Equal(42245.16m, D(data, "valorTotal"));
        Assert.Equal(2112.26m, D(data, "valores.valorIss"));
        Assert.Equal("1-Tributação no município", S(data, "valores.naturezaOperacao"));
    }

    [SampleTheory]
    [InlineData("dcomp1.pdf", 2, 750352.54)]
    [InlineData("dcomp2.pdf", 14, 1669937.72)]
    public void Dcomp(string sample, int debitos, decimal total)
    {
        var data = Run(sample, "dcomp").Data;
        var items = data["debitos"]!.AsArray();
        Assert.Equal(debitos, items.Count);
        Assert.Equal(total, D(data, "totalDebitos"));
        Assert.Equal(total, items.Sum(d => d!["total"]!.GetValue<decimal>()));
        Assert.Equal("Lucro Real", S(data, "credito.formaTributacao"));
        Assert.Equal("MARCOS PAOLUCCI SANTOS PINTO", S(data, "responsavelPJ.nome"));
    }

    [SampleFact]
    public void PerdcompRessarcimento()
    {
        var data = Run("perdcom.pdf", "perdcomp-ressarcimento").Data;
        Assert.Equal("COCAL COMERCIO INDUSTRIA CANAA ACUCAR E ALCOOL SA", S(data, "nomeEmpresarial"));
        Assert.Equal("93176-8", S(data, "dadosBancarios.conta"));
        Assert.Equal(2, data["consolidacaoCreditos"]!.AsArray().Count);
        Assert.Equal(17009.62m, D(data, "saldoCredito.creditos[0].valores[0].Abril"));
        Assert.Equal(971377.12m, D(data, "saldoCredito.creditos[1].valores[0].Total"));
    }

    [SampleFact]
    public void DeclaracaoImportacao()
    {
        var data = Run("declaracao_importacao.pdf", "declaracao-importacao").Data;
        Assert.Equal("23/0137707-4", S(data, "numeroDeclaracao"));
        Assert.Equal("MATCHEM - PE PRODUTOS QUIMICOS LTDA.", S(data, "importador.nome"));
        Assert.Equal(13095.19m, D(data, "tributos[1].recolhido"));
        Assert.Equal(11, data["dadosComplementares"]!["despachantes"]!.AsArray().Count);
        Assert.Equal("3906.90.19", S(data, "adicoes[0].ncm"));
        Assert.Equal(38882.63m, D(data, "adicoes[0].cofins.valorARecolher"));
    }

    [SampleFact]
    public void GuiaFgtsDigital()
    {
        var data = Run("guia_recisoria_fgts.pdf", "guia-fgts-digital").Data;
        Assert.Equal("GOOGLE BRASIL INTERNET LTDA.", S(data, "razaoSocialEmpregador"));
        Assert.Equal("07/07/2026 09:38", S(data, "tag"));
        Assert.Equal("0126070748017117-2", S(data, "identificador"));
        Assert.Equal(215399.05m, D(data, "valorARecolher"));
        Assert.Equal(2, data["composicao"]!.AsArray().Count);
        Assert.Equal(193535.77m, D(data, "composicao[0].indenizacaoCompensatoria"));
    }

    [Fact]
    public void AllSavedTemplatesAreValid()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "src", "PocPdfPig.Api", "Templates"), "*.json"))
        {
            var (template, errors) = TemplateParser.Parse(File.ReadAllText(file));
            Assert.True(template is not null, $"{Path.GetFileName(file)}: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void InvalidTemplateReturnsFriendlyErrors()
    {
        var (template, errors) = TemplateParser.Parse("""{ "fields": [ { "name": "x", "type": "labelRight" }, { "name": "y", "type": "regex", "pattern": "(" } ] }""");
        Assert.Null(template);
        Assert.Contains(errors, e => e.Contains("label"));
        Assert.Contains(errors, e => e.Contains("regex inválida"));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PocPdfPig.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("PocPdfPig.sln não encontrado.");
    }
}
