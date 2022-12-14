using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.UI.Suite.Core;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OpenIddict.UI.Infrastructure;

public class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _repository;
    private readonly IOpenIddictApplicationClaimRepository _claimRepository;
    private readonly IOpenIddictApplicationClaimService _openIddictApplicationClaimService;
    private readonly OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication> _manager;

    public ApplicationService(
      IApplicationRepository repository,
      IOpenIddictApplicationClaimRepository openIddictApplicationClaimRepository,
      IOpenIddictApplicationClaimService openIddictApplicationClaimService,
      OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication> manager
    )
    {
        _repository = repository
          ?? throw new ArgumentNullException(nameof(repository));
        _claimRepository = openIddictApplicationClaimRepository ?? throw new ArgumentNullException(nameof(openIddictApplicationClaimRepository));

        _openIddictApplicationClaimService = openIddictApplicationClaimService ?? throw new ArgumentNullException(nameof(openIddictApplicationClaimService));
        _manager = manager
      ?? throw new ArgumentNullException(nameof(manager));
    }

    public async Task<IEnumerable<ApplicationInfo>> GetApplicationsAsync()
    {
        var items = await _repository.ListAsync(new AllApplications());

        return items.Select(x =>
        {
            return ToListInfo(x);
        });
    }

    public async Task<ApplicationInfo> GetAsync(string id)
    {
        if (id == null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        var entity = await _manager.FindByIdAsync(id);

        if (entity == null) return null;

        var claims = await _claimRepository.ListAsync(new AllOpenIddictApplicationClaimByApplications(id));

        return  ToInfo(entity, claims.ToList());
    }

    public async Task<string> CreateAsync(ApplicationParam model)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var entity = await _manager.FindByClientIdAsync(model.ClientId);
        if (entity == null)
        {
            // create new entity
            var newEntity = new OpenIddictEntityFrameworkCoreApplication
            {
                ClientId = model.ClientId,
                DisplayName = model.DisplayName,
                Type = model.Type
            };

            HandleCustomProperties(model, newEntity);

            await _manager.CreateAsync(newEntity, model.ClientSecret);

            return newEntity.Id;
        }

        // update existing entity
        model.Id = entity.Id;
        await UpdateAsync(model);

        return entity.Id;
    }

    public async Task UpdateAsync(ApplicationParam model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new InvalidOperationException(nameof(model.Id));
        }

        var entity = await _manager.FindByIdAsync(model.Id);

        SimpleMapper.Map(model, entity);

        HandleCustomProperties(model, entity);

        await _manager.UpdateAsync(entity, entity.ClientSecret);

        var claims = model.Claims.Select(x => new OpenIddictApplicationClaim { ClaimType = x.ClaimType, ClaimValue = x.ClaimValue }).ToList();

        await AssignClaimsAsync(model.Id, claims);

    }

    private async Task AssignClaimsAsync(string applicationId, List<OpenIddictApplicationClaim> claims)
    {
        // removing claims
        await _openIddictApplicationClaimService.RemoveApplicationClaimsAsync(applicationId);

        //add new claims
        await _openIddictApplicationClaimService.AddApplicationClaimsAsync(claims, applicationId);
    }


    public async Task DeleteAsync(string id)
    {
        if (id == null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        var entity = await _manager.FindByIdAsync(id);

        await _manager.DeleteAsync(entity);
    }

    private static ApplicationInfo ToListInfo(OpenIddictEntityFrameworkCoreApplication entity) => SimpleMapper.From<OpenIddictEntityFrameworkCoreApplication, ApplicationInfo>(entity);

    private static ApplicationInfo ToInfo(OpenIddictEntityFrameworkCoreApplication entity, List<OpenIddictApplicationClaim> claims)
    {
        var info = SimpleMapper
          .From<OpenIddictEntityFrameworkCoreApplication, ApplicationInfo>(entity);

        info.RequireConsent = entity.ConsentType == ConsentTypes.Explicit;
        info.Permissions = entity.Permissions != null
          ? JsonSerializer.Deserialize<List<string>>(entity.Permissions)
          : new List<string>();
        info.RedirectUris = entity.RedirectUris != null
          ? JsonSerializer.Deserialize<List<string>>(entity.RedirectUris)
          : new List<string>();
        info.PostLogoutRedirectUris = entity.PostLogoutRedirectUris != null
          ? JsonSerializer.Deserialize<List<string>>(entity.PostLogoutRedirectUris)
          : new List<string>();
        info.RequirePkce = entity.Requirements != null && JsonSerializer
          .Deserialize<List<string>>(entity.Requirements)
          .Contains(Requirements.Features.ProofKeyForCodeExchange);
        info.Claims = claims.Select(a => new OpenIddictApplicationClaimInfo { ClaimType = a.ClaimType, ClaimValue = a.ClaimValue }).ToList();

        return info;
    }

    private static void HandleCustomProperties(
      ApplicationParam model,
      OpenIddictEntityFrameworkCoreApplication entity
    )
    {
        entity.ConsentType = model.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit;
        entity.Permissions = JsonSerializer.Serialize(model.Permissions);
        entity.RedirectUris = JsonSerializer.Serialize(model.RedirectUris);
        entity.PostLogoutRedirectUris = JsonSerializer.Serialize(model.PostLogoutRedirectUris);
        entity.Requirements = model.RequirePkce ? JsonSerializer.Serialize(new List<string> {
      Requirements.Features.ProofKeyForCodeExchange
    }) : null;
    }
}
