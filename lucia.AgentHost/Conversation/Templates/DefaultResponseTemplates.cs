namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Seed data for the response templates collection.
/// These are inserted on first run or when <see cref="IResponseTemplateRepository.ResetToDefaultsAsync"/> is called.
/// </summary>
public static class DefaultResponseTemplates
{
    /// <summary>Returns the built-in response templates for all known skill actions.</summary>
    public static IReadOnlyList<ResponseTemplate> GetDefaults() =>
    [
        // LightControlSkill
        new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates =
            [
                "OK, I turned {action} the {entity}.",
                "Done! The {entity} is now {action}.",
                "{entity} turned {action}.",
            ],
            IsDefault = true,
        },
        new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "brightness",
            Templates =
            [
                "OK, I set the {entity} brightness to {value} percent.",
                "Done! {entity} brightness is now {value}%.",
            ],
            IsDefault = true,
        },

        // ClimateControlSkill
        new ResponseTemplate
        {
            SkillId = "ClimateControlSkill",
            Action = "set_temperature",
            Templates =
            [
                "OK, I set the thermostat to {value} degrees.",
                "Done! Temperature set to {value}\u00b0.",
            ],
            IsDefault = true,
        },
        new ResponseTemplate
        {
            SkillId = "ClimateControlSkill",
            Action = "adjust",
            Templates =
            [
                "OK, I'm making it {action}.",
                "Adjusting the temperature \u2014 making it {action}.",
            ],
            IsDefault = true,
        },

        // SceneControlSkill
        new ResponseTemplate
        {
            SkillId = "SceneControlSkill",
            Action = "activate",
            Templates =
            [
                "OK, activating the {scene} scene.",
                "Done! The {scene} scene is now active.",
            ],
            IsDefault = true,
        },
    ];
}
