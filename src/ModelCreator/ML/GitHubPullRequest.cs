using Microsoft.ML.Data;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace ModelCreator.ML;

public class GitHubPullRequest
{
    [LoadColumn(0)]
    public string CombinedID;

    [LoadColumn(1)]
    public float ID;

    [LoadColumn(2)]
    public string Area;

    [LoadColumn(3)]
    public string Title;

    [LoadColumn(4)]
    public string Description;

    [LoadColumn(5)]
    public string Author;

    [LoadColumn(6)]
    public float IsPR;

    [LoadColumn(7)]
    public string UserMentions;

    [LoadColumn(8)]
    public float NumMentions;
    
    [LoadColumn(9)]
    public float FileCount;

    [LoadColumn(10)]
    public string Files;

    [LoadColumn(11)]
    public string Filenames;

    [LoadColumn(12)]
    public string FileExtensions;

    [LoadColumn(13)]
    public string FolderNames;

    [LoadColumn(14)]
    public string Folders;
}
