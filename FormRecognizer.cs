using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;

namespace form_recognizer_function
{
    public static class FormRecognizer
    {
        [FunctionName("FormRecognizer")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string endpoint = Environment.GetEnvironmentVariable("FORM_RECOGNIZER_END_POINT");
            string key = Environment.GetEnvironmentVariable("FORM_RECOGNIZER_KEY");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic requestBodyResult = JsonConvert.DeserializeObject(requestBody);

            if (requestBodyResult == null || requestBodyResult.values == null)
                return new BadRequestObjectResult(new { Error = "Form Url and Form SaS Token is required" });

            AzureKeyCredential credential = new AzureKeyCredential(key);
            DocumentAnalysisClient client = new DocumentAnalysisClient(new Uri(endpoint), credential);

            var response = new List<FormRecognizerResponse>();
            TableFormater[] tableFormater;

            foreach (var value in requestBodyResult.values)
            {

                if (value.data == null || value.data.formUrl == null || value.data.formSasToken == null || value.recordId == null)
                    continue;

                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, "prebuilt-layout", new Uri($"{value.data.formUrl}?{value.data.formSasToken}"));
                tableFormater = new TableFormater[operation.Value.Tables.Count];

                for (int indexTable = 0; indexTable < operation.Value.Tables.Count; indexTable++)
                {
                    tableFormater[indexTable] = new TableFormater();

                    for (int line = 0; line < operation.Value.Tables[indexTable].Cells.Count; line++)
                    {
                        if (operation.Value.Tables[indexTable].Cells[line].Kind == DocumentTableCellKind.ColumnHeader)
                        {
                            tableFormater[indexTable].DataTableFormaters.Add(new DataTableFormater { Column = operation.Value.Tables[indexTable].Cells[line].Content, ColumnIndex = operation.Value.Tables[indexTable].Cells[line].ColumnIndex });
                            continue;
                        }

                        tableFormater[indexTable].DataTableFormaters.Find(df => df.ColumnIndex == operation.Value.Tables[indexTable].Cells[line].ColumnIndex).Values.Add(operation.Value.Tables[indexTable].Cells[line].Content);
                    }
                }

                response.Add(
                    new FormRecognizerResponse()
                    {
                        RecordId = value.recordId,
                        Data = new DocumentContentResult()
                        {
                            Tables = tableFormater.Select(table => new DataTableResult { Table = table.DataTableFormaters.Select(dataTable => new TableResult { Column = dataTable.Column, Values = dataTable.Values }) }),
                            Paragraphs = operation.Value.Paragraphs.Select(p => p.Content),
                            Content = operation.Value.Content
                        }
                    }
                );
            }


            return new OkObjectResult(new
            {
                Values = response
            });
        }

        class FormRecognizerResponse
        {
            public string RecordId { get; set; }
            public DocumentContentResult Data { get; set; }
            public string[] Errors { get; set; }
            public string[] Warnings { get; set; }
        }

        class DocumentContentResult
        {
            public string Content { get; set; }
            public IEnumerable<string> Paragraphs { get; set; }
            public IEnumerable<DataTableResult> Tables { get; set; }
        }

        class DataTableResult
        {
            public IEnumerable<TableResult> Table { get; set; }
        }

        class TableResult
        {
            public string Column { get; set; }
            public List<string> Values { get; set; }
        }

        class TableFormater
        {
            public List<DataTableFormater> DataTableFormaters { get; set; } = new List<DataTableFormater>();
        }

        class DataTableFormater
        {
            public string Column { get; set; }
            public int ColumnIndex { get; set; }
            public List<string> Values { get; set; } = new List<string>();
        }
    }
}
