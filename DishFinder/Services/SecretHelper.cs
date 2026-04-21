using Google.Cloud.SecretManager.V1;

namespace DishFinder.Services
{
    public class SecretHelper
    {
        public static string ReadSecret(string projectId, string secretName)
        {
            var client = SecretManagerServiceClient.Create();
            var versionName = new SecretVersionName(projectId, secretName, "latest");
            var response = client.AccessSecretVersion(versionName);
            return response.Payload.Data.ToStringUtf8();
        }
    }
}