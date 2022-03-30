namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public interface IModelHolderFactory
    {
        IModelHolder CreateModelHolder(string owner, string repo);
    }
}
