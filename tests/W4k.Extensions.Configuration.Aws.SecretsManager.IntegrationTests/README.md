## W4k AWS Secrets Manager, Integration tests

In order to run the integration tests, you need to have a valid AWS credentials set up on your machine.
Integration tests require `w4ktest@admin` profile (see [`SecretsManagerTestFixture.cs`](./SecretsManagerTestFixture.cs)), it must be set up in your AWS credentials file (`~/.aws/credentials`).

```ini
[w4ktest]
aws_access_key_id     = 00000000000000000000
aws_secret_access_key = 0000000000000000000000000000000000000000
aws_session_token     = ...
account               = ...
role                  = ...

[w4ktest@admin]
role_arn       = arn:aws:iam::000000000000:role/my-role@admin
source_profile = w4ktest
```

Once credentials are set up, you can run the integration tests as any other tests in the solution.

To run only unit tests (and skip integration tests), filter out tests by category:

```bash
dotnet test --filter "TestCategory!=Integration"
```