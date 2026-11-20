using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hortiplanner.Api.Transfer;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class TransferTests(PostgresFixture database)
{
    [Fact]
    public void Spreadsheet_cells_are_quoted_and_formulas_kept_as_text()
    {
        Assert.Equal("\"Hilltop, Bakewell\"", Csv.Cell("Hilltop, Bakewell"));
        Assert.Equal("\"Say \"\"hello\"\"\"", Csv.Cell("Say \"hello\""));
        Assert.Equal("'=HYPERLINK(1)", Csv.Cell("=HYPERLINK(1)"));
        Assert.Equal("-3", Csv.Cell(-3));
        Assert.Equal("+44 1629 123456", Csv.Cell("+44 1629 123456"));
        Assert.Equal("'+SUM(A1)", Csv.Cell("+SUM(A1)"));
        Assert.Equal("Yes", Csv.Cell(true));
        Assert.Equal("2026-11-12", Csv.Cell(new DateOnly(2026, 11, 12)));
    }

    private static object Row(int row, object values) => new { row, values };

    [Fact]
    public async Task Every_problem_row_is_named_and_nothing_is_saved_until_there_are_none()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", variety = "Munstead", xylellaHost = true, protectedVariety = false });

        object[] rows =
        [
            Row(2, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Sage", ["botanicalName"] = "Salvia officinalis", ["xylellaHost"] = "yes" }),
            Row(3, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "", ["variety"] = "Alba" }),
            Row(4, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Thyme", ["protectedVariety"] = "maybe" }),
            Row(5, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "sage" }),
            Row(6, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "lavender", ["variety"] = "munstead" }),
        ];

        var checkedRows = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows, save = true }));
        Assert.False(checkedRows.GetProperty("saved").GetBoolean());
        Assert.Equal([3, 4, 5], checkedRows.GetProperty("problems").EnumerateArray().Select(problem => problem.GetProperty("row").GetInt32()));
        Assert.Contains("Common name is empty", checkedRows.GetProperty("problems")[0].GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.Contains("yes or no", checkedRows.GetProperty("problems")[1].GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.Equal([6], checkedRows.GetProperty("alreadyThere").EnumerateArray().Select(row => row.GetInt32()));
        Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray());

        object[] fixedRows = [rows[0], Row(4, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Thyme", ["protectedVariety"] = "no" }), rows[4]];
        var saved = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows = fixedRows, save = true }));
        Assert.True(saved.GetProperty("saved").GetBoolean());
        Assert.Equal(2, saved.GetProperty("adding").GetInt32());

        var plants = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray().ToList();
        Assert.Equal(3, plants.Count);
        Assert.True(plants.Single(plant => string.Equals(plant.GetProperty("commonName").GetString(), "Sage", StringComparison.Ordinal)).GetProperty("xylellaHost").GetBoolean());

        var again = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows = fixedRows, save = true }));
        Assert.Equal(0, again.GetProperty("adding").GetInt32());
        Assert.Equal(3, again.GetProperty("alreadyThere").GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/import/Customers", new { rows = fixedRows, save = false }));
    }

    [Fact]
    public async Task A_plants_VAT_rate_goes_out_in_the_plants_sheet_and_comes_back_in()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Basil", protectedVariety = false, vatBand = "Zero" });

        using (var response = await nursery.Owner.GetAsync(Route("/api/export"), Cancellation))
        {
            await using var zipped = await response.Content.ReadAsStreamAsync(Cancellation);
            using var archive = new ZipArchive(zipped);
            using var plants = new StreamReader(archive.GetEntry("plants.csv")!.Open(), Encoding.UTF8);
            var lines = (await plants.ReadToEndAsync(Cancellation)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains(",Protected variety,VAT rate,No longer used", lines[0], StringComparison.Ordinal);
            Assert.EndsWith(",No,Zero,No", lines[1], StringComparison.Ordinal);
        }

        object[] rows =
        [
            Row(2, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Parsley", ["vatBand"] = "zero rated" }),
            Row(3, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Compost", ["vatBand"] = "Reduced" }),
            Row(4, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Lavender" }),
            Row(5, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Mint", ["vatBand"] = "0%" }),
            Row(6, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Rose", ["vatBand"] = "luxury" }),
        ];

        var checkedRows = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows, save = false }));
        var problem = Assert.Single(checkedRows.GetProperty("problems").EnumerateArray());
        Assert.Equal(6, problem.GetProperty("row").GetInt32());
        Assert.Equal("VAT rate should be standard, reduced or zero, not \"luxury\".", problem.GetProperty("problem").GetString());

        Assert.True(Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows = rows[..4], save = true })).GetProperty("saved").GetBoolean());

        var bands = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray()
            .ToDictionary(plant => plant.GetProperty("commonName").GetString()!, plant => plant.GetProperty("vatBand").GetString(), StringComparer.Ordinal);
        Assert.Equal("Zero", bands["Parsley"]);
        Assert.Equal("Reduced", bands["Compost"]);
        Assert.Equal("Standard", bands["Lavender"]);
        Assert.Equal("Zero", bands["Mint"]);
    }

    [Fact]
    public async Task Every_cost_said_for_a_batch_comes_out_with_its_VAT_and_who_said_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", protectedVariety = false })).GetProperty("id").GetGuid();
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-3), plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var batch = started.GetProperty("batchId").GetGuid();
        await PostForTextAsync(nursery.Staff, $"/api/batches/{batch}/costs", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, kind = "Material", answer = "Amount", amount = 2.99m });
        var first = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/costing"), Cancellation)).GetProperty("material").GetProperty("entry").GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, $"/api/batches/{batch}/costs", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, kind = "Material", answer = "Amount", amount = 2.99m, vatBand = "Zero", includesVat = true, replacesId = first });

        using var response = await nursery.Owner.GetAsync(Route("/api/export"), Cancellation);
        await using var zipped = await response.Content.ReadAsStreamAsync(Cancellation);
        using var archive = new ZipArchive(zipped);
        using var costs = new StreamReader(archive.GetEntry("batch-costs.csv")!.Open(), Encoding.UTF8);
        var lines = (await costs.ReadToEndAsync(Cancellation)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.EndsWith("Batch,Kind,Answer,Amount typed,Typed amount includes VAT,VAT rate (%),VAT said by,With VAT,Before VAT,VAT,Note,Added by,Added,Replaced", lines[0], StringComparison.Ordinal);
        Assert.Contains(",Seed or plants,Amount,2.99,Yes,20.00,Assumed,2.99,2.49,0.50,,Alex Moss,", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",Yes", lines[1], StringComparison.Ordinal);
        Assert.Contains(",Seed or plants,Amount,2.99,Yes,0.00,Sam Fielding,2.99,2.99,0.00,,Sam Fielding,", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",No", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Everything_comes_out_in_one_download()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "=Lavender, the best", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, "/api/customers", new { name = "Hilltop Garden Centre" });
        var tray = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-3), plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" })).GetProperty("containerId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }));

        using var response = await nursery.Owner.GetAsync(Route("/api/export"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("hortiplanner-records-", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName, StringComparison.Ordinal);

        await using var zipped = await response.Content.ReadAsStreamAsync(Cancellation);
        using var archive = new ZipArchive(zipped);
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "work.csv", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "count-adjustments.csv", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "legacy-counts.csv", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "batch-costs.csv", StringComparison.Ordinal));

        using var plants = new StreamReader(archive.GetEntry("plants.csv")!.Open(), Encoding.UTF8);
        var text = await plants.ReadToEndAsync(Cancellation);
        Assert.StartsWith("Common name,", text, StringComparison.Ordinal);
        Assert.Contains("\"'=Lavender, the best\"", text, StringComparison.Ordinal);

        using var work = new StreamReader(archive.GetEntry("work.csv")!.Open(), Encoding.UTF8);
        var lines = (await work.ReadToEndAsync(Cancellation)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("Line effective time", lines[0], StringComparison.Ordinal);
        Assert.Contains(",Sown,", lines[1], StringComparison.Ordinal);
        Assert.Contains(",40,", lines[1], StringComparison.Ordinal);

        using var trays = new StreamReader(archive.GetEntry("trays-now.csv")!.Open(), Encoding.UTF8);
        Assert.Contains(",40,No,Germinated,", await trays.ReadToEndAsync(Cancellation), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/export"));
    }
}
