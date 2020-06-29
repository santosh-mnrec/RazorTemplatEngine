﻿using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.FileProviders;
using RazorTemplatEngine.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace RazorTemplatEngine
{
    /// <summary>
    /// https://docs.microsoft.com/en-us/aspnet/core/razor-pages/sdk?view=aspnetcore-3.1#properties
    /// </summary>
    public sealed class RazorTemplateEngineV2
    {
        private const string TemplateFolderName = "Templates";
        private readonly Dictionary<string, RazorCompiledItem> _razorCompiledItems = new Dictionary<string, RazorCompiledItem>();
        private readonly EmbeddedFileProvider _embeddedFileProvider;

        public RazorTemplateEngineV2()
        {
            var thisAssembly = Assembly.GetExecutingAssembly();
            var viewAssembly = RelatedAssemblyAttribute.GetRelatedAssemblies(thisAssembly, false).Single();
            var razorCompiledItems = new RazorCompiledItemLoader().LoadItems(viewAssembly);

            foreach (var item in razorCompiledItems)
            {
                _razorCompiledItems.Add(item.Identifier, item);
            }

            _embeddedFileProvider = new EmbeddedFileProvider(thisAssembly);
        }

        public async Task<string> RenderTemplateAsync<TModel>(TModel model)
        {
            var templateNamePrefix = model.GetType().Name;
            EnsureAllTemplatesExist(TemplateFolderName, templateNamePrefix);

            using var stringWriter = new StringWriter();
            await LoadResource(TemplateFolderName, templateNamePrefix, ResourceType.Header, stringWriter);
            await stringWriter.WriteAsync(await RenderTemplateAsync(TemplateFolderName, templateNamePrefix, model));
            await LoadResource(TemplateFolderName, templateNamePrefix, ResourceType.Footer, stringWriter);

            stringWriter.Flush();
            return stringWriter.ToString();
        }

        private void EnsureAllTemplatesExist(string templateFolderName, string templateNamePrefix)
        {
            var razorTemplate = GetRazorTemplateName(templateFolderName, templateNamePrefix);
            var headerEmbeddedResourceName = GetEmbeddedResourceName(templateFolderName, templateNamePrefix, ResourceType.Header);
            var footerEmbeddedResourceName = GetEmbeddedResourceName(templateFolderName, templateNamePrefix, ResourceType.Footer);

            var errorMessages = new StringBuilder();
            if (!_razorCompiledItems.TryGetValue(razorTemplate, out var razorCompiledItem))
            {
                errorMessages.AppendLine($"The Razor Template file: {razorTemplate}, was not found.");
            }

            var resourceFiles = new[] { headerEmbeddedResourceName, footerEmbeddedResourceName };
            foreach (var resourceFile in resourceFiles)
            {
                try
                {
                    var stream = _embeddedFileProvider.GetFileInfo(resourceFile).CreateReadStream();
                    stream.Close();
                }
                catch (FileNotFoundException)
                {
                    errorMessages.AppendLine($"The Embedded Resource File: {resourceFile}, was not found.");
                }
            }

            if (errorMessages.Length > 0)
            {
                throw new RazorTemplateNotFoundException(errorMessages.ToString());
            }
        }

        private async Task LoadResource(string templateFolderName, string templateNamePrefix, ResourceType resourceType, TextWriter textWriter)
        {
            var embeddedResourceName = GetEmbeddedResourceName(templateFolderName, templateNamePrefix, resourceType);
            var resourceStream = _embeddedFileProvider.GetFileInfo(embeddedResourceName).CreateReadStream();
            using var streamReader = new StreamReader(resourceStream);

            var buffer = new char[1024];
            int bytesRead = 0;
            while ((bytesRead = await streamReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await textWriter.WriteAsync(buffer, 0, bytesRead);
            }
        }

        private async Task<string> RenderTemplateAsync<TModel>(string templateFolderName, string templateName, TModel model)
        {
            var razorTemplate = GetRazorTemplateName(templateFolderName, templateName);
            var razorCompiledItem = _razorCompiledItems[razorTemplate];
            return await GetRenderedOutput(razorCompiledItem, model);
        }

        private static string GetRazorTemplateName(string templateFolderName, string templateName)
        {
            return $"/{templateFolderName}/{templateName}.cshtml";
        }

        private static string GetEmbeddedResourceName(string templateFolderName, string templateNamePrefix, ResourceType resourceType)
        {
            return $"{templateFolderName}.{templateNamePrefix}{resourceType}.html";
        }

        private static async Task<string> GetRenderedOutput<TModel>(RazorCompiledItem razorCompiledItem, TModel model)
        {
            using var stringWriter = new StringWriter();
            var razorPage = GetRazorPageInstance(razorCompiledItem, model, stringWriter);
            await razorPage.ExecuteAsync();
            return stringWriter.ToString();
        }

        private static RazorPage GetRazorPageInstance<TModel>(RazorCompiledItem razorCompiledItem, TModel model, TextWriter textWriter)
        {
            var razorPage = (RazorPage<TModel>)Activator.CreateInstance(razorCompiledItem.Type);

            razorPage.ViewData = new ViewDataDictionary<TModel>(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
            {
                Model = model
            };

            razorPage.ViewContext = new ViewContext
            {
                Writer = textWriter
            };

            razorPage.HtmlEncoder = HtmlEncoder.Default;
            return razorPage;
        }

        private enum ResourceType { Header, Footer }
    }
}
