
public class AuthResult
{
    public bool Ok {get;set;}
    public string? DisplayName {get;set;}
    public string? Id {get;set;}
    public string? Error {get;set;}
    public AuthInfo? LoginInfo {get;set;}
}


public class AuthInfo
{
    public string UserCode {get;set;} = "";
    public string VerificationUrl {get;set;} = "";
    public string Message {get;set;} = "";
}