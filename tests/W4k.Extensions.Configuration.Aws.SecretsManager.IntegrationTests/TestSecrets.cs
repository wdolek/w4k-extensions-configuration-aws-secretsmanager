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

    public const string BinarySecretName = "w4k/awssm/binary-secret";
    public const string BinarySecretValue = "The cake is a lie.";

    public const string InvalidUtf8BinarySecretName = "w4k/awssm/invalid-utf8-binary-secret";
    // 0xC3 0x28 is an invalid UTF-8 sequence (continuation byte expected)
    public static readonly byte[] InvalidUtf8BinarySecretValue = [0xC3, 0x28];

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

    public const string PlainTextSecretName = "w4k/awssm/plain-text-secret";
    public const string PlainTextSecretValue = "L3_S3cr37";
}