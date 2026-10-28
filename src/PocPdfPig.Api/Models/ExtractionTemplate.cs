namespace PocPdfPig.Api.Models;

/// <summary>
/// Template (payload) que descreve QUAIS informações extrair de um PDF e COMO localizá-las.
/// Veja docs/02-referencia-template.md para a referência completa.
/// </summary>
public sealed class ExtractionTemplate
{
    /// <summary>Nome do template (apenas informativo, volta no response).</summary>
    public string? Name { get; set; }

    /// <summary>Descrição livre do documento/template.</summary>
    public string? Description { get; set; }

    /// <summary>Configurações gerais de leitura do PDF.</summary>
    public TemplateSettings Settings { get; set; } = new();

    /// <summary>Campos a extrair. Cada campo gera uma propriedade no JSON de saída.</summary>
    public List<FieldDefinition> Fields { get; set; } = new();
}

/// <summary>Configurações gerais de como o PDF é lido e "montado" em linhas/células.</summary>
public sealed class TemplateSettings
{
    /// <summary>Páginas a considerar. Ex.: "1", "1-3", "1,3,5-7". Vazio = todas.</summary>
    public string? Pages { get; set; }

    /// <summary>Diferença vertical máxima (em pontos) para duas palavras serem consideradas da mesma linha. Padrão 3.</summary>
    public double LineTolerance { get; set; } = 3;

    /// <summary>Distância horizontal mínima (em pontos) entre palavras para separar "células" de uma linha. Padrão 8.</summary>
    public double CellGap { get; set; } = 8;

    /// <summary>Ignorar acentos ao comparar rótulos e regex (ex.: "Período" casa com "Periodo"). Padrão true.</summary>
    public bool IgnoreAccents { get; set; } = true;

    /// <summary>Diferenciar maiúsculas/minúsculas ao comparar rótulos e regex. Padrão false.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Regex de linhas a descartar do documento inteiro (cabeçalhos/rodapés repetidos).</summary>
    public List<string> IgnoreLines { get; set; } = new();

    /// <summary>Algoritmo de palavras do PdfPig: "default" ou "nearestNeighbour".</summary>
    public string WordExtractor { get; set; } = "default";

    /// <summary>Cultura usada para converter números/datas. Padrão "pt-BR".</summary>
    public string Culture { get; set; } = "pt-BR";
}

/// <summary>Opções de pós-processamento do valor (comuns a campos e colunas de tabela).</summary>
public class ValueOptions
{
    /// <summary>Comentário livre (ignorado pelo motor). Útil para documentar o template.</summary>
    public string? Description { get; set; }

    /// <summary>Regex aplicada ao texto bruto para isolar o valor (usa o grupo "value", senão o grupo 1, senão o match inteiro).</summary>
    public string? ValuePattern { get; set; }

    /// <summary>Tipo de saída: string (padrão), decimal, integer, date, datetime, boolean.</summary>
    public string DataType { get; set; } = "string";

    /// <summary>Formato de data (ex.: "dd/MM/yyyy") ou cultura para decimal (ex.: "en-US").</summary>
    public string? Format { get; set; }

    /// <summary>Transformações: trim, upper, lower, digitsOnly, removeSpaces, collapseSpaces.</summary>
    public List<string>? Transforms { get; set; }

    /// <summary>Textos que devem ser tratados como "sem valor" (ex.: ["-", "N/A"]).</summary>
    public List<string>? NullValues { get; set; }

    /// <summary>Valor padrão quando nada for encontrado.</summary>
    public string? Default { get; set; }
}

/// <summary>Definição de um campo a extrair.</summary>
public sealed class FieldDefinition : ValueOptions
{
    /// <summary>Nome da propriedade no JSON de saída.</summary>
    public string Name { get; set; } = "";

    /// <summary>Estratégia: labelRight, labelBelow, regex, region, table, group, metadata, formField.</summary>
    public string Type { get; set; } = "";

    /// <summary>Se true e o valor não for encontrado, o resultado sai com success=false.</summary>
    public bool Required { get; set; }

    // ----- Localização por rótulo (labelRight, labelBelow, region relativa) -----

    /// <summary>Texto do rótulo (busca "contém", respeitando início/fim de palavra).</summary>
    public string? Label { get; set; }

    /// <summary>Regex do rótulo (alternativa a Label). Ex.: "^Total\\b".</summary>
    public string? LabelPattern { get; set; }

    /// <summary>Qual ocorrência do rótulo usar: 1 = primeira (padrão), 2 = segunda, -1 = última.</summary>
    public int Occurrence { get; set; } = 1;

    /// <summary>Restringe a busca a uma página (1 = primeira).</summary>
    public int? Page { get; set; }

    // ----- labelRight -----

    /// <summary>labelRight: "cell" (padrão, só o bloco de texto logo após o rótulo) ou "line" (tudo à direita).</summary>
    public string Take { get; set; } = "cell";

    /// <summary>labelRight: limita a quantidade de palavras do valor.</summary>
    public int? MaxWords { get; set; }

    /// <summary>labelRight/labelBelow: concatena linhas seguintes que continuam o valor (quebra de linha).</summary>
    public bool Multiline { get; set; }

    // ----- labelBelow -----

    /// <summary>labelBelow: quantas linhas abaixo do rótulo compõem o valor. Padrão 1.</summary>
    public int Lines { get; set; } = 1;

    // ----- regex -----

    /// <summary>regex: expressão regular aplicada ao texto do escopo (linhas separadas por \n).</summary>
    public string? Pattern { get; set; }

    /// <summary>regex: nome/índice do grupo a retornar.</summary>
    public string? Group { get; set; }

    /// <summary>regex: true retorna TODAS as ocorrências (array).</summary>
    public bool All { get; set; }

    // ----- region -----

    /// <summary>region: retângulo (coordenadas PDF, origem no canto inferior esquerdo).</summary>
    public RegionDefinition? Region { get; set; }

    // ----- table -----

    /// <summary>table: texto que identifica a linha de cabeçalho da tabela.</summary>
    public string? Header { get; set; }

    /// <summary>table: regex que identifica a linha de cabeçalho (alternativa a Header).</summary>
    public string? HeaderPattern { get; set; }

    /// <summary>table: colunas a extrair. Se vazio, todas as colunas do cabeçalho são retornadas.</summary>
    public List<ColumnDefinition>? Columns { get; set; }

    /// <summary>table: regex que uma linha precisa casar para ser considerada linha de dados.</summary>
    public string? RowPattern { get; set; }

    /// <summary>table: anexa linhas que não casam com RowPattern à linha anterior (células quebradas).</summary>
    public bool MergeContinuationLines { get; set; }

    /// <summary>table: inclui no resultado colunas do cabeçalho que não foram declaradas em Columns.</summary>
    public bool IncludeOtherColumns { get; set; }

    // ----- group -----

    /// <summary>group: regex da linha que INICIA cada bloco.</summary>
    public string? Start { get; set; }

    /// <summary>group/table: regex da linha que ENCERRA o bloco/tabela (a linha não é incluída).</summary>
    public string? End { get; set; }

    /// <summary>group: true = retorna array com todos os blocos; false = só o primeiro (objeto).</summary>
    public bool Repeat { get; set; }

    /// <summary>group: cada página vira um bloco.</summary>
    public bool PerPage { get; set; }

    /// <summary>group: regex de linhas a descartar dentro do bloco (cabeçalhos/rodapés).</summary>
    public List<string>? IgnoreLines { get; set; }

    /// <summary>group: campos extraídos dentro de cada bloco.</summary>
    public List<FieldDefinition>? Fields { get; set; }

    // ----- metadata / formField -----

    /// <summary>metadata: Title, Author, Subject, Keywords, Creator, Producer, CreationDate, ModifiedDate, PageCount, Version.
    /// formField: nome do campo de formulário (AcroForm).</summary>
    public string? Key { get; set; }
}

/// <summary>Coluna de uma tabela.</summary>
public sealed class ColumnDefinition : ValueOptions
{
    /// <summary>Nome da propriedade no JSON de saída.</summary>
    public string Name { get; set; } = "";

    /// <summary>Texto do cabeçalho da coluna (como aparece no PDF).</summary>
    public string? Header { get; set; }

    /// <summary>Regex do cabeçalho da coluna (alternativa a Header).</summary>
    public string? HeaderPattern { get; set; }

    /// <summary>Limite esquerdo fixo (x em pontos). Use com Right quando a coluna não tem cabeçalho.</summary>
    public double? Left { get; set; }

    /// <summary>Limite direito fixo (x em pontos).</summary>
    public double? Right { get; set; }
}

/// <summary>Retângulo em coordenadas PDF (pontos; origem no canto inferior esquerdo da página).</summary>
public sealed class RegionDefinition
{
    /// <summary>Página (1 = primeira). Ignorado quando o campo usa Label (região relativa).</summary>
    public int? Page { get; set; }

    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Right { get; set; }
    public double Top { get; set; }
}
