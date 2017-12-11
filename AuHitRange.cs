using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

// entities ////////////////////////////////////////////////////////////////////
public partial class Player {
    //[SyncVar] int test;

    public partial class PlayerLevel {
		[Range(1, 100)] public float baseHitRange = 1;
    }

    // hitRange
    public float baseHitRange { get { return levels[level-1].baseHitRange; } }

    public override float hitRange {
        get {
            // calculate equipment bonus
			float equipmentBonus = 
				(from item in equipment
                              where item.valid
                              select item.equipHitRangeBonus).Sum();
            // calculate buff bonus
				float buffBonus = 
					(from skill in skills
                             where skill.BuffTimeRemaining() > 0
                             select skill.buffsHitRange).Sum();
            return baseHitRange + equipmentBonus + buffBonus;
        }
    }

	public void CastSkill(Skill skill){
		AuCastSkill (skill);
	}

    void Awake_AuHitRange() {}
    void OnStartLocalPlayer_AuHitRange() {}
    void OnStartServer_AuHitRange() {}
    void Start_AuHitRange() {}
    void UpdateClient_AuHitRange() {}
    void LateUpdate_AuHitRange() {}
    void OnDestroy_AuHitRange() {}
    [Server] void OnDeath_AuHitRange() {}
    [Client] void OnSelect_AuHitRange(Entity entity) {}
    [Server] void OnLevelUp_AuHitRange() {}
	[Server] void OnUseInventoryItem_AuHitRange(int index) {}
	[Server] void DealDamageAt_AuHitRange(HashSet<Entity> entities, int amount) {}

    // you can use the drag and drop system too:
    void OnDragAndDrop_InventorySlot_AuHitRangeSlot(int[] slotIndices) {}
    void OnDragAndClear_AuHitRangeSlot(int slotIndex) {}



	// custom DealDamageAt function that also rewards experience if we killed
	// the monster
	[Server]
	public override HashSet<Entity> DealDamageAt(Entity entity, int amount, float aoeRadius=0, float hitRange=1) {
		// deal damage with the default function. get all entities that were hit
		// in the AoE radius
		var entities = base.DealDamageAt(entity, amount, aoeRadius, hitRange);
		foreach (var e in entities) {
			// a monster?
			if (e is Monster) {
				OnDamageDealtToMonster((Monster)e);
				// a player?
				// (see murder code section comments to understand the system)
			} else if (e is Player) {
				OnDamageDealtToPlayer((Player)e);
				// a pet?
				// (see murder code section comments to understand the system)
			} else if (e is Pet) {
				OnDamageDealtToPet((Pet)e);
			}
		}

		// let pet know that we attacked something
		if (activePet != null && activePet.autoAttack)
			activePet.OnAggro(entity);

		// addon system hooks
		Utils.InvokeMany(typeof(Player), this, "DealDamageAt_", entities, amount);

		return entities; // not really needed anywhere
	}
}

public partial class Monster {
    void Awake_AuHitRange() {}
    void OnStartServer_AuHitRange() {}
    void Start_AuHitRange() {}
    void UpdateClient_AuHitRange() {}
    void LateUpdate_AuHitRange() {}
    [Server] void OnAggro_AuHitRange(Entity entity) {}
	[Server] void OnDeath_AuHitRange() {}
	public void CastSkill(Skill skill){
		AuCastSkill (skill);
	}
	[Header("AuXtra HitRange")]
	[SerializeField] float _hitRange = 1;
	public override float hitRange { get { return _hitRange; } }
}

public partial class Npc {
    void OnStartServer_AuHitRange() {}
	void UpdateClient_AuHitRange() {}
	public void CastSkill(Skill skill){
		AuCastSkill (skill);
	}
	public override float hitRange { get { return 0; } }
}

public partial class Pet {
    public partial class PetLevel {
    }

    void Awake_AuHitRange() {}
    void OnStartServer_AuHitRange() {}
    void Start_AuHitRange() {}
    void UpdateClient_AuHitRange() {}
    void LateUpdate_AuHitRange() {}
    void OnDestroy_AuHitRange() {}
    [Server] void OnLevelUp_AuHitRange() {}
    [Server] void DealDamageAt_AuHitRange(HashSet<Entity> entities, int amount) {}
    [Server] void OnAggro_AuHitRange(Entity entity) {}
	[Server] void OnDeath_AuHitRange() {}
	public void CastSkill(Skill skill){
		AuCastSkill (skill);
	}

	[Header("AuXtra HitRange")]
	[SerializeField] float _hitRange = 1;
	public override float hitRange { get { return _hitRange; } }
}

public partial class Entity {

	public abstract float hitRange { get; }

    void Awake_AuHitRange() {}
    void OnStartServer_AuHitRange() {}
    void Update_AuHitRange() {}
	[Server] void DealDamageAt_AuHitRange(HashSet<Entity> entities, int amount) {}

	// deal damage at another entity
	// (can be overwritten for players etc. that need custom functionality)
	// (can also return the set of entities that were hit, just in case they are
	//  needed when overwriting it)
	[Server]
	public virtual HashSet<Entity> DealDamageAt(Entity entity, int amount, float aoeRadius=0, float hitRange=1) {
		// build the set of entities that were hit within AoE range
		var entities = new HashSet<Entity>();

		// add main target in any case, because non-AoE skills have radius=0
		entities.Add(entity);

		// add all targets in AoE radius around main target
		var colliders = Physics.OverlapSphere(entity.transform.position, aoeRadius); //, layerMask);
		foreach (var co in colliders) {
			var candidate = co.GetComponentInParent<Entity>();
			// overlapsphere cast uses the collider's bounding volume (see
			// Unity scripting reference), hence is often not exact enough
			// in our case (especially for radius 0.0). let's also check the
			// distance to be sure.
			// => we also check CanAttack so that Npcs can't be killed by AoE
			//    damage etc.
			if (candidate != null && candidate != this && CanAttack(candidate) &&
				Vector3.Distance(entity.transform.position, candidate.transform.position) < aoeRadius)
				entities.Add(candidate);
		}

		// now deal damage at each of them
		foreach (var e in entities) {
			int damageDealt = 0;
			var popupType = PopupType.Normal;

			// don't deal any damage if target is invincible
			if (!e.invincible) {
				float eFloat = Vector3.Distance(transform.position, e.transform.position);
				//Debug.LogWarning("Look at eFloat ----------------> " + eFloat);

				//Debug.LogWarning("Look at hitRange ----------------> " + hitRange);
				if (eFloat < hitRange) {
					// block? (we use < not <= so that block rate 0 never blocks)
					if (UnityEngine.Random.value < e.blockChance) {
						popupType = PopupType.Block;
						// deal damage
					} else {
						// subtract defense (but leave at least 1 damage, otherwise
						// it may be frustrating for weaker players)
						damageDealt = Mathf.Max (amount - e.defense, 1);

						// critical hit?
						if (UnityEngine.Random.value < criticalChance) {
							damageDealt *= 2;
							popupType = PopupType.Crit;
						}

						// deal the damage
						e.health -= damageDealt;
					}
				}
			}

			if (damageDealt > 0) {
				// show damage popup in observers via ClientRpc
				RpcShowDamagePopup (e.gameObject, popupType, damageDealt);
				//todo add color values based on percentage of highest value.
			}
			// let's make sure to pull aggro in any case so that archers
			// are still attacked if they are outside of the aggro range
			e.OnAggro(this);
		}

		// addon system hooks
		Utils.InvokeMany(typeof(Entity), this, "DealDamageAt_", entities, amount);

		return entities;
	}



	// casts the skill. casting and waiting has to be done in the state machine
	public void AuCastSkill(Skill skill) {
		// check self again (alive, mana, weapon etc.). ignoring the skill cd
		// and check target again
		// note: we don't check the distance again. the skill will be cast even
		// if the target walked a bit while we casted it (it's simply better
		// gameplay and less frustrating)
		if (CastCheckSelf(skill, false) && CastCheckTarget(skill)) {
			// do the logic in here or let the skill effect take care of it?
			if (skill.effectPrefab == null || skill.effectPrefab.isPurelyVisual) {
				// attack
				if (skill.category == "Attack") {
					// deal damage directly
					DealDamageAt(target, damage + skill.damage, skill.aoeRadius, hitRange);
					// heal
				} else if (skill.category == "Heal") {
					// note: 'target alive' checks were done above already
					target.health += skill.healsHealth;
					target.mana += skill.healsMana;
					// buff
				} else if (skill.category == "Buff") {
					// set the buff end time (the rest is done in .damage etc.)
					skill.buffTimeEnd = Time.time + skill.buffTime;
				}
			}

			// spawn the skill effect (if any)
			SpawnSkillEffect(currentSkill, target);

			// decrease mana in any case
			mana -= skill.manaCosts;

			// start the cooldown (and save it in the struct)
			skill.cooldownEnd = Time.time + skill.cooldown;

			// save any skill modifications in any case
			skills[currentSkill] = skill;
		} else {
			// not all requirements met. no need to cast the same skill again
			currentSkill = -1;
		}
	}

}

// items ///////////////////////////////////////////////////////////////////////
public partial class ItemTemplate {
    //[Header("My Addon")]
    //public int addonVariable = 0;

	[Header("AuHitRange")]
	public int equipHitRangeBonus;
}

// note: can't add variables yet without modifying original constructor
public partial struct Item {
    //public int addonVariable {
    //    get { return template.addonVariable; }
    //}

	void ToolTip_AuHitRange(StringBuilder tip) {
		tip.Replace("{EQUIPHITRANGEBONUS}", equipHitRangeBonus.ToString());
        //tip.Append("");
    }

	public float equipHitRangeBonus {
		get { return template.equipHitRangeBonus; }
	}
}

// skills //////////////////////////////////////////////////////////////////////
public partial class SkillTemplate {

    public partial struct SkillLevel {
        // note: adding variables here will give lots of warnings, but it works.
		//public int addonVariable;
		[Header("AuHitRange")]
		public float buffsHitRange;
    }
}

// note: can't add variables yet without modifying original constructor
public partial struct Skill {
    //public int addonVariable {
    //    get { return template.addonVariable; }
    //}

	public float buffsHitRange {
		get { return template.levels[level-1].buffsHitRange; }
	}
    void ToolTip_AuHitRange(StringBuilder tip) {
		tip.Replace("{BUFFSHITRANGE}", buffsHitRange.ToString());
    }
}
