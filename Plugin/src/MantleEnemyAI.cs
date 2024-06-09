using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MantleEnemy
{
    class MantleEnemyAI : EnemyAI
    {
        public AudioSource audioSource;
        public AudioClip mantle_footstep;
        public AudioClip mantle_roar;
        public AudioClip roam_sound;

        public Transform turnCompass = null!;
        public float normalSpeed = 3f;
        public float chaseSpeed = 5f;
        public float chargeSpeed = 15f;
        public float fleeSpeed = 7f;
        public float stunDuration = 3f;
        public float damageAmount = 10f;
        public float maxHealth = 100f;
        public float wanderRadius = 20f;
        public float wanderTimer = 5f;
        private Animator animator;
        private bool isCharging = false;
        private bool isStunned = false;
        private bool isFleeing = false;
        private float currentHealth;
        private float stunEndTime;

        private bool knockbackApplied = false;
        private float knockbackCooldownTime = 1f;
        private float lastKnockbackTime = -Mathf.Infinity;
        private NavMeshAgent navMeshAgent;
        private float timer;

        private float footstepTimer = 0f;
        private float footstepInterval = 0.5f;
        private float healthThreshold = 20f; // Flee when health is below this value
        private float lastStateChangeTime = 0f;
        private float changeStateInterval = 5f; // Intervalle de changement d'état

        enum State
        {
            SearchingForPlayer,
            MovingTowardsPlayer,
            Patrolling,
            Fleeing
        }

        public override void Start()
        {
            base.Start();
            animator = GetComponent<Animator>();
            navMeshAgent = GetComponent<NavMeshAgent>();

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                Debug.LogWarning("AudioSource is missing from the enemy prefab");
            }
            else
            {
                // Assurez-vous que l'AudioSource est configurée pour le son 3D
                audioSource.spatialBlend = 1.0f; // 3D sound
                audioSource.minDistance = 1.0f;
                audioSource.maxDistance = 20.0f; // Ajustez en fonction de votre jeu
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            }

            if (mantle_footstep == null)
            {
                Debug.LogWarning("mantle_footstep is not assigned in the inspector");
            }

            if (mantle_roar == null)
            {
                Debug.LogWarning("mantle_roar is not assigned in the inspector");
            }

            if (roam_sound == null)
            {
                Debug.LogWarning("roam_sound is not assigned in the inspector");
            }

            currentHealth = maxHealth;
            currentBehaviourStateIndex = (int)State.Patrolling;
            navMeshAgent.speed = normalSpeed;

            timer = wanderTimer;

            StartCoroutine(PlayRandomRoar());
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead)
            {
                return;
            }

            if (isStunned)
            {
                if (Time.time >= stunEndTime)
                {
                    isStunned = false;
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
                else
                {
                    navMeshAgent.speed = 0f;
                    return;
                }
            }

            if (currentHealth < healthThreshold && !isFleeing)
            {
                isFleeing = true;
                SwitchToBehaviourClientRpc((int)State.Fleeing);
            }

            var state = currentBehaviourStateIndex;

            if (Time.time > lastStateChangeTime + changeStateInterval)
            {
                lastStateChangeTime = Time.time;
            }

            switch (state)
            {
                case (int)State.MovingTowardsPlayer:
                    HandleMovingTowardsPlayer();
                    break;
                case (int)State.Patrolling:
                    HandlePatrolling();
                    break;
                case (int)State.Fleeing:
                    HandleFleeing();
                    break;
                default:
                    navMeshAgent.speed = normalSpeed;
                    animator.SetBool("isWalking", false);
                    break;
            }

            if (knockbackApplied && state != (int)State.MovingTowardsPlayer)
            {
                knockbackApplied = false;
            }
        }

        private void HandleMovingTowardsPlayer()
        {
            navMeshAgent.speed = isCharging ? chargeSpeed : chaseSpeed;
            animator.SetBool("isCharging", isCharging);
            animator.SetBool("isWalking", !isCharging);
            animator.SetFloat("WalkSpeedMultiplier", isCharging ? 2.0f : 1.0f);

            footstepTimer += Time.deltaTime;
            if (footstepTimer >= footstepInterval / (isCharging ? 2.0f : 1.0f))
            {
                PlayMantleFootstep();
                footstepTimer = 0f;
            }
        }

        private void HandlePatrolling()
        {
            timer += Time.deltaTime;

            if (timer >= wanderTimer)
            {
                Vector3 newPos = RandomNavSphere(transform.position, wanderRadius, -1);
                navMeshAgent.SetDestination(newPos);
                timer = 0;
            }

            if (!navMeshAgent.pathPending && navMeshAgent.remainingDistance < 0.5f)
            {
                timer = wanderTimer; // Force new destination on next update
            }

            animator.SetBool("isWalking", true);
        }

        private void HandleFleeing()
        {
            navMeshAgent.speed = fleeSpeed;
            Vector3 fleeDirection = (transform.position - targetPlayer.transform.position).normalized;
            Vector3 fleePosition = transform.position + fleeDirection * 10f; // Flee distance

            SetDestinationToPosition(fleePosition, checkForPath: true);
            animator.SetBool("isWalking", true);
        }

        public static Vector3 RandomNavSphere(Vector3 origin, float dist, int layermask)
        {
            Vector3 randDirection = Random.insideUnitSphere * dist;
            randDirection += origin;
            NavMeshHit navHit;
            NavMesh.SamplePosition(randDirection, out navHit, dist, layermask);
            return navHit.position;
        }

        public void ApplyDamage(float damage)
        {
            currentHealth -= damage;
            if (currentHealth <= 0)
            {
                Die();
            }
        }

        private void Die()
        {
            isEnemyDead = true;
            navMeshAgent.isStopped = true;
            animator.SetTrigger("Die");
            // Additional logic for enemy death
        }

        void OnTriggerEnter(Collider other)
        {
            if ((other.CompareTag("Player")) && Time.time > lastKnockbackTime + knockbackCooldownTime)
            {
                Debug.Log("Player or Obstacle Hit by Enemy!");

                isStunned = true;
                stunEndTime = Time.time + stunDuration;
                isCharging = false;
                animator.SetBool("isCharging", false);
                animator.SetTrigger("ChargeAtk");
                PlayRoamSound();
                navMeshAgent.speed = 0f;

                if (other.CompareTag("Player"))
                {
                    PlayerControllerB playerController = other.GetComponent<PlayerControllerB>();
                    if (playerController != null)
                    {
                        Vector3 hitDirection = (other.transform.position - transform.position).normalized;
                        StartCoroutine(ApplyKnockback(playerController, hitDirection));

                        playerController.DamagePlayer((int)damageAmount);
                    }

                    knockbackApplied = true;
                    lastKnockbackTime = Time.time;
                }

                StartCoroutine(TransitionToIdleAfterCharge());
            }
        }

        private IEnumerator TransitionToIdleAfterCharge()
        {
            yield return new WaitForSeconds(1f);

            animator.SetBool("isWalking", false);
            animator.SetBool("isCharging", false);
            animator.ResetTrigger("ChargeAtk");

            if (currentBehaviourStateIndex == (int)State.MovingTowardsPlayer)
            {
                isCharging = false;
                currentBehaviourStateIndex = (int)State.Patrolling;
            }

            StartCoroutine(RestartSearchingForPlayer());
        }

        private IEnumerator RestartSearchingForPlayer()
        {
            yield return new WaitForSeconds(stunDuration);
            if (!isEnemyDead)
            {
                SwitchToBehaviourClientRpc((int)State.Patrolling);
            }
        }

        private IEnumerator ApplyKnockback(PlayerControllerB playerController, Vector3 hitDirection)
        {
            playerController.enabled = false;

            float knockbackForce = 10f;
            float upwardsModifier = 0.2f;

            Vector3 force = new Vector3(hitDirection.x, upwardsModifier, hitDirection.z).normalized * knockbackForce;

            CharacterController controller = playerController.GetComponent<CharacterController>();
            if (controller != null)
            {
                for (float timer = 0; timer < 0.5f; timer += Time.deltaTime)
                {
                    controller.Move(force * Time.deltaTime);
                    yield return null;
                }
            }

            playerController.enabled = true;

            RaycastHit hit;
            if (Physics.Raycast(playerController.transform.position, force.normalized, out hit, 1f))
            {
                if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Default"))
                {
                    controller.Move(Vector3.zero);
                }
            }
        }
        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }
            switch (currentBehaviourStateIndex)
            {
                case (int)State.Patrolling:
                    navMeshAgent.speed = normalSpeed;
                    if (FoundClosestPlayerInRange(30f, 5f))
                    {
                        SwitchToBehaviourClientRpc((int)State.MovingTowardsPlayer);
                    }
                    break;
                case (int)State.MovingTowardsPlayer:
                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 25 && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        SwitchToBehaviourClientRpc((int)State.Patrolling);
                        return;
                    }
                    MoveTowardsPlayer();
                    break;
                default:
                    break;
            }
        }

        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase()
        {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                float tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            return targetPlayer != null;
        }

        void MoveTowardsPlayer()
        {
            if (targetPlayer == null || !IsOwner)
            {
                return;
            }
            SetDestinationToPosition(targetPlayer.transform.position, checkForPath: false);
            isCharging = true;
            animator.SetBool("isCharging", true);
            animator.SetBool("isWalking", false);
        }

        private void SetDestinationToPosition(Vector3 position, bool checkForPath = true)
        {
            if (checkForPath)
            {
                NavMeshPath path = new NavMeshPath();
                if (navMeshAgent.CalculatePath(position, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    navMeshAgent.SetDestination(position);
                }
            }
            else
            {
                navMeshAgent.SetDestination(position);
            }
        }

        public void PlayMantleFootstep()
        {
            if (audioSource != null && mantle_footstep != null)
            {
                audioSource.PlayOneShot(mantle_footstep);
            }
            else
            {
                Debug.LogWarning("AudioSource or FootstepClip is not assigned");
            }
        }

        public void PlayRoamSound()
        {
            if (audioSource != null && roam_sound != null)
            {
                audioSource.PlayOneShot(roam_sound);
            }
            else
            {
                Debug.LogWarning("AudioSource or RoamClip is not assigned");
            }
        }

        private IEnumerator PlayRandomRoar()
        {
            while (!isEnemyDead)
            {
                float waitTime = Random.Range(10f, 20f);
                yield return new WaitForSeconds(waitTime);

                PlayMantleRoar();
            }
        }

        public void PlayMantleRoar()
        {
            if (audioSource != null && mantle_roar != null)
            {
                audioSource.PlayOneShot(mantle_roar);
            }
            else
            {
                Debug.LogWarning("AudioSource or RoarClip is not assigned");
            }
        }
    }
}
