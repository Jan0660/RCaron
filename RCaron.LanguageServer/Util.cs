﻿using System.Collections.Concurrent;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RCaron.LanguageServer;

public static class Util
{
    public static DocumentSelector DocumentSelector { get; } =
        new(new DocumentFilter()
        {
            Language = "rcaron",
            Scheme = "file"
        });

    public static ConcurrentDictionary<string, string> DocumentTexts = new();

    public static async Task<string> GetDocumentTextAsync(string fileSystemPath)
    {
        if (DocumentTexts.TryGetValue(fileSystemPath, out var value))
            return value;
        return DocumentTexts[fileSystemPath] = await System.IO.File.ReadAllTextAsync(fileSystemPath);
    }

    public static (int Line, int Column) GetLineAndColumn(int index, in string raw, int? startLine, int? startColumn,
        int? startOffset)
    {
        const int indexedByWhat = 0; // or 1
        var line = startLine ?? indexedByWhat;
        var col = startColumn ?? indexedByWhat;
        for (var i = startOffset ?? indexedByWhat; i < index; i++)
        {
            col++;
            if (raw[i] == '\n')
            {
                line++;
                col = indexedByWhat;
            }
        }

        return (line, col);
    }

    public static Position GetPosition(int index, in string raw, int? startLine = null, int? startColumn = null,
        int? startOffset = null)
    {
        var (line, column) = GetLineAndColumn(index, raw, startLine, startColumn, startOffset);
        return new Position(line, column);
    }

    public static int GetPositionInt(string raw, Position position)
    {
        const int indexedByWhat = 0; // or 1
        var line = indexedByWhat;
        var col = indexedByWhat;
        for (var i = indexedByWhat; i < raw.Length; i++)
        {
            col++;
            if (line == position.Line && col == position.Character)
            {
                return i + 1;
            }

            if (raw[i] == '\n')
            {
                line++;
                col = indexedByWhat;
            }
        }

        throw new();
    }

    public static Range GetRange(int start, int end, in string raw)
    {
        var startPos = GetPosition(start, raw);
        var endPos = GetPosition(end, raw, startPos.Line, startPos.Character, start);
        return new Range(startPos, endPos);
    }
}