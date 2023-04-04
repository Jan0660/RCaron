using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RCaron.AutoCompletion;
using RCaron.BaseLibrary;
using RCaron.Shell;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

namespace RCaron.LanguageServer;

public class CompletionHandler : CompletionHandlerBase
{
    private readonly ILogger<CompletionHandler> _logger;

    private static readonly IRCaronModule[] Modules = {
        new ShellStuffModule(new()),
        new ExperimentalModule(),
        new LoggingModule(),
    };

    public CompletionHandler(ILogger<CompletionHandler> logger)
        => _logger = logger;

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = Util.DocumentSelector,
            ResolveProvider = false,
        };

    public override async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        => CompletionList.From(await GetCompletionsAsync(request.TextDocument, request.Position,
            cancellationToken: cancellationToken));

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
        => throw new();

    public async Task<ICollection<CompletionItem>> GetCompletionsAsync(TextDocumentIdentifier textDocument,
        Position caretPosition, int maxCompletions = 40, CancellationToken cancellationToken = default)
    {
        var list = new List<CompletionItem>(maxCompletions);
        var code = await Util.GetDocumentTextAsync(textDocument.Uri.GetFileSystemPath());
        cancellationToken.ThrowIfCancellationRequested();
        var caretPositionInt = Util.GetPositionInt(code, caretPosition);
        var completions = new CompletionProvider().GetCompletions(
            code, caretPositionInt, maxCompletions, null, Modules);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var completion in completions)
        {
            list.Add(new CompletionItem
            {
                Label = completion.Thing.Word,
                TextEdit = new TextEditOrInsertReplaceEdit(new InsertReplaceEdit()
                {
                    Replace = Util.GetRange(completion.Position.Start, completion.Position.End, code),
                    NewText = completion.Thing.Word,
                }),
                Kind = (CompletionItemKind)completion.Thing.Kind,
                Detail = completion.Thing.Detail,
                Documentation = completion.Thing.Documentation == null
                    ? null
                    : new StringOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = completion.Thing.Documentation,
                    }),
                Deprecated = completion.Thing.Deprecated,
            });
        }

        return list;
    }
}