using UnityEngine;
using UnityEngine.Events;

namespace Cyanotype.World
{
    public enum WorldInteractableKind
    {
        ProceduralBotanical,  // Harvestable 3D plant/fern specimen
        ProceduralRock,       // Carryable 3D rock / paper weight
        HumanNPC2D,           // 2D animated human in 3D world
        CyanotypeWorkbench    // Switches player into ActiveCyanotypeArmsOnly mode
    }

    public class WorldInteractableItem : MonoBehaviour
    {
        [SerializeField] private WorldInteractableKind _kind = WorldInteractableKind.ProceduralBotanical;
        [SerializeField] private string _promptLabel = "Gather Botanical Specimen";
        [SerializeField] private bool _destroyOnHarvest = false;
        [SerializeField] private UnityEvent<CyanotypeWorldInteractor> _onInteracted;

        public WorldInteractableKind Kind => _kind;
        public string PromptLabel => _promptLabel;

        public void Interact(CyanotypeWorldInteractor actor)
        {
            switch (_kind)
            {
                case WorldInteractableKind.HumanNPC2D:
                    if (TryGetComponent<Cyanotype2DNPCController>(out var npc))
                    {
                        npc.TriggerConversation();
                    }
                    break;

                case WorldInteractableKind.CyanotypeWorkbench:
                    var modeRig = actor.GetComponent<CyanotypeFirstPersonRigMode>();
                    if (modeRig != null)
                    {
                        modeRig.SetMode(PlayerPresentationMode.ActiveCyanotypeArmsOnly);
                    }
                    break;

                case WorldInteractableKind.ProceduralBotanical:
                    Debug.Log($"[Cyanotype World] Harvested: {_promptLabel}");
                    _onInteracted?.Invoke(actor);
                    if (_destroyOnHarvest) gameObject.SetActive(false);
                    return;

                case WorldInteractableKind.ProceduralRock:
                    actor.CarryWorldObject(gameObject);
                    break;
            }

            _onInteracted?.Invoke(actor);
        }

#if UNITY_EDITOR
        public void ConfigureProcedural(WorldInteractableKind kind, string label, bool destroyOnHarvest)
        {
            _kind = kind;
            _promptLabel = label;
            _destroyOnHarvest = destroyOnHarvest;
        }

        public void ConfigureNPC(string npcName)
        {
            _kind = WorldInteractableKind.HumanNPC2D;
            _promptLabel = $"Speak with {npcName}";
            _destroyOnHarvest = false;
        }
#endif
    }
}