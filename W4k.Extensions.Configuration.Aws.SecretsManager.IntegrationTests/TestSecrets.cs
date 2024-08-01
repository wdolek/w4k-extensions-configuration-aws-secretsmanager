namespace W4k.Extensions.Configuration.Aws.SecretsManager.IntegrationTests;

public static class TestSecrets
{
    public const string KeyValueSecretName = "w4k/awssm/key-value-secret";
    public const string KeyValueJson = """
        {
            "ClientId": "my_client_id",
            "ClientSecret": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
        }
        """;

    public const string ComplexSecretName = "w4k/awssm/complex-secret";
    public const string ComplexJson = """
        {
            "MyService__Username": "saanvis",
            "ApiKeys": {
                "Citizenship": "rosebud",
                "Universe": "42"
            },
            "PIN": [ 5, 5, 5, 2, 3, 6, 8 ]
        }
        """;
}