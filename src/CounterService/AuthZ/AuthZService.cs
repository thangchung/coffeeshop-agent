namespace CounterService.AuthZ;

public interface IAuthZService
{
    string MapUserToRole(string email);
}

internal class StuffAuthZService : IAuthZService
{
    public string MapUserToRole(string email)
    {
        if (email is "a2a_mcp_admin@thangchungonthenetgmail.onmicrosoft.com")
        {
            return "admin";
        }
        else
        {
            return "normal_user";
        }
    }
}
