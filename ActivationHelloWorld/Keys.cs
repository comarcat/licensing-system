namespace ActivationHelloWorld;

public static class Keys
{
    // Public half of the RSA key LicensingApi signs license files with — safe to embed,
    // see docs/activation-dll-integration-reference.md §5.5.
    public const string LicensingPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAt6fBjOqh1XA5RCBl/feB
        ogm6Hn/ZZSBWv/10PjavfquYPq6+IYckg03bnBzzUeELj3LvWjAunOfgJ3qQ3OsW
        Pij0e76FLmb4EJbUTTe/ycKDKPjwG0mtWHRt7tmav+NzsxuBuTVC6vN4Tzxzyhvf
        Z6i8l3sfljy/9boxSkbxBAlvL2ZwYVT0ukD00xm4Ycx0l+D3PRDLdGGEsCqrT3cO
        iZ5XOxVYve8eddD0eMPUGFw69MfSKleZzSrhMDmieMXZWomU0AAjA5X2fuXZfN89
        THwiGD6Q/Pxil99Hh4Hm+9+oj9K+nu7kJqxTNNVB/tp9HJD67xqdIWs7VQoJCPDw
        DOE4uwy/frvp8xvcahDcyTUJ5AkD3sk6/UlamHddsdQWT6iT5D3jqybcjjmpHnuC
        SGqzcz+0joqlakzmRQUhWlA7EhANELZvBFI2Iz/wABkSFGpNsdqOzRNvOobUnxk9
        N+0hXYgWO0XYb0ay0BqosLLAvRYElioZyDMVKkGR4Xh7AgMBAAE=
        -----END PUBLIC KEY-----
        """;
}
