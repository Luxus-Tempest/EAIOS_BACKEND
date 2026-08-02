using EAIOS.Api.Contracts;
using FluentValidation;

namespace EAIOS.Api.Application.Common.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'adresse email est requise.")
            .EmailAddress().WithMessage("L'adresse email n'est pas valide.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Le mot de passe est requis.");
    }
}
