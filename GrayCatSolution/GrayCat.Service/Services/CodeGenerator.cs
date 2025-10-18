namespace GrayCat.Service.Services;

using GrayCat.Core.Interfaces;
using GrayCat.Core.Models;
using GrayCat.Shared.Models;

public class CodeGenerator : ICodeGenerator
{
    private readonly ILogger<CodeGenerator> _logger;
    private readonly ITemplateEngine _templateEngine;

    public CodeGenerator(ILogger<CodeGenerator> logger, ITemplateEngine templateEngine)
    {
        _logger = logger;
        _templateEngine = templateEngine;
    }

    public async Task<string> GenerateComponentCodeAsync(BlockModel block, ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.React or ProjectType.NextJs => await GenerateReactComponentAsync(block),
            ProjectType.ReactNative => await GenerateReactNativeComponentAsync(block),
            _ => throw new NotSupportedException($"Project type {projectType} not supported for component generation")
        };
    }

    public async Task<string> GeneratePageCodeAsync(List<BlockModel> blocks, ProjectType projectType)
    {
        var components = new List<string>();

        foreach (var block in blocks)
        {
            var code = await GenerateComponentCodeAsync(block, projectType);
            components.Add(code);
        }

        return string.Join("\n\n", components);
    }

    public async Task<Dictionary<string, string>> GenerateProjectFilesAsync(ProjectModel project)
    {
        var files = new Dictionary<string, string>();

        // Generate main app file
        var appCode = await GenerateAppFileAsync(project);
        files["App.js"] = appCode;

        // Generate components
        foreach (var block in project.Blocks)
        {
            var componentCode = await GenerateComponentCodeAsync(block, project.Type);
            files[$"components/{block.Type}Block.jsx"] = componentCode;
        }

        return files;
    }

    private async Task<string> GenerateReactComponentAsync(BlockModel block)
    {
        var props = string.Join(", ", block.Properties.Keys);
        var styles = GenerateInlineStyles(block.Styles);

        return $@"import React from 'react';

const {block.Type}Block = ({{ {props} }}) => {{
  return (
    <div className=""{block.Type.ToLower()}-block"" style={{{styles}}}>
      <h2>{{title || '{block.Type}'}}</h2>
      {(block.Content != string.Empty ? $"<p>{block.Content}</p>" : "")}
    </div>
  );
}};

export default {block.Type}Block;";
    }

    private async Task<string> GenerateReactNativeComponentAsync(BlockModel block)
    {
        var props = string.Join(", ", block.Properties.Keys);

        return $@"import React from 'react';
import {{ View, Text, StyleSheet }} from 'react-native';

const {block.Type}Block = ({{ {props} }}) => {{
  return (
    <View style={{styles.container}}>
      <Text style={{styles.title}}>{{title || '{block.Type}'}}</Text>
      {(block.Content != string.Empty ? $"<Text>{{content}}</Text>" : "")}
    </View>
  );
}};

const styles = StyleSheet.create({{
  container: {{
    padding: 16,
    backgroundColor: '#fff',
  }},
  title: {{
    fontSize: 20,
    fontWeight: 'bold',
  }},
}});

export default {block.Type}Block;";
    }

    private async Task<string> GenerateAppFileAsync(ProjectModel project)
    {
        var imports = string.Join("\n", project.Blocks
            .Select(b => $"import {b.Type}Block from './components/{b.Type}Block';"));

        var components = string.Join("\n      ", project.Blocks
            .OrderBy(b => b.PositionY)
            .ThenBy(b => b.PositionX)
            .Select(b => $"<{b.Type}Block />"));

        return $@"import React from 'react';
import './App.css';
{imports}

function App() {{
  return (
    <div className=""App"">
      {components}
    </div>
  );
}}

export default App;";
    }

    private string GenerateInlineStyles(Dictionary<string, string> styles)
    {
        if (!styles.Any()) return "{}";

        var styleEntries = styles.Select(kvp => $"{kvp.Key}: '{kvp.Value}'");
        return $"{{ {string.Join(", ", styleEntries)} }}";
    }
}