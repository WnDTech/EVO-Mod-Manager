using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IProfileService
{
    List<Profile> GetAll();
    Profile? GetById(string id);
    Profile? GetByName(string name);
    void Create(Profile profile);
    void Update(Profile profile);
    void Delete(string id);
    Task ActivateAsync(string profileId, string modsFolder);
    string ExportToJson(string profileId);
    Profile ImportFromJson(string json);
}
