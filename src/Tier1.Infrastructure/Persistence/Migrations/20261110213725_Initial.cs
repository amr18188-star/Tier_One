using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.EnsureSchema(
                name: "logs");

            migrationBuilder.EnsureSchema(
                name: "kpi");

            migrationBuilder.EnsureSchema(
                name: "adm");

            migrationBuilder.EnsureSchema(
                name: "org");

            migrationBuilder.CreateTable(
                name: "access_state",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chain_anchors",
                schema: "logs",
                columns: table => new
                {
                    chain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    first_seq = table.Column<long>(type: "bigint", nullable: false),
                    last_seq = table.Column<long>(type: "bigint", nullable: false),
                    last_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    event_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intact = table.Column<bool>(type: "boolean", nullable: true),
                    broken_at_seq = table.Column<long>(type: "bigint", nullable: true),
                    verify_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chain_anchors", x => new { x.chain, x.day });
                });

            migrationBuilder.CreateTable(
                name: "chain_checkpoints",
                schema: "logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    chain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_to = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chain_checkpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chain_state",
                schema: "logs",
                columns: table => new
                {
                    chain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_day = table.Column<DateOnly>(type: "date", nullable: true),
                    hmac_from_seq = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chain_state", x => x.chain);
                });

            migrationBuilder.CreateTable(
                name: "conflicts",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope = table.Column<List<string>>(type: "text[]", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    value_a = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    value_b = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    proposal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    preparation_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    prepared_by = table.Column<Guid>(type: "uuid", nullable: true),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    deciding_body = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decision_date = table.Column<DateOnly>(type: "date", nullable: true),
                    minutes_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decision_recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conflicts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "adm",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    name_en = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "mail_outbox",
                schema: "adm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body_protected = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    audience = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "account"),
                    module = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "ptl"),
                    beneficiary_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    related_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    related_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_ref = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    skipped_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resent_from_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_templates",
                schema: "adm",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    module = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    event_bound = table.Column<bool>(type: "boolean", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    subject_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body_ar = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    body_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    text_version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    chain_seq = table.Column<long>(type: "bigint", nullable: true),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    hash_alg = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pillars",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pillars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    grants_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "adm",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "structure_units",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    duties = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_head = table.Column<bool>(type: "boolean", nullable: false),
                    per_department = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_structure_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    established_on = table.Column<DateOnly>(type: "date", nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                    table.CheckConstraint("ck_units_code", "code ~ '^[A-Z0-9][A-Z0-9-]{0,31}$'");
                    table.ForeignKey(
                        name: "fk_units_units_merged_into_id",
                        column: x => x.merged_into_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_units_units_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    full_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    full_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret_protected = table.Column<string>(type: "text", nullable: true),
                    last_totp_step = table.Column<long>(type: "bigint", nullable: false),
                    failed_logins = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_mfa = table.Column<int>(type: "integer", nullable: false),
                    mfa_locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preferred_language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "standards",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pillar_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    source_pages = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    classification_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standards", x => x.id);
                    table.ForeignKey(
                        name: "fk_standards_pillars_pillar_id",
                        column: x => x.pillar_id,
                        principalSchema: "kpi",
                        principalTable: "pillars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rbac_changes",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    base_version = table.Column<int>(type: "integer", nullable: false),
                    proposed_json = table.Column<string>(type: "text", nullable: false),
                    diff_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rbac_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_rbac_changes_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_grants",
                schema: "iam",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    caps = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    specials = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_grants", x => new { x.role_id, x.feature });
                    table.ForeignKey(
                        name: "fk_role_grants_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "structure_positions",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    single = table.Column<bool>(type: "boolean", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    suggested_role_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_structure_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_structure_positions_structure_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "org",
                        principalTable: "structure_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "industry_partners",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    college_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sector = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    coordinator_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    coordinator_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    coordinator_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    agreement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    agreement_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_industry_partners", x => x.id);
                    table.ForeignKey(
                        name: "fk_industry_partners_units_college_id",
                        column: x => x.college_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_industry_partners_units_department_id",
                        column: x => x.department_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_changes",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    effective_on = table.Column<DateOnly>(type: "date", nullable: false),
                    old_value = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    new_value = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    by = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_unit_changes_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mfa_required = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_overrides",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    caps = table.Column<int>(type: "integer", nullable: false),
                    specials = table.Column<List<string>>(type: "text[]", nullable: false),
                    effect = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_overrides_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_values = table.Column<List<string>>(type: "text[]", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indicators",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    standard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    suspended = table.Column<bool>(type: "boolean", nullable: false),
                    conflict_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicators", x => x.id);
                    table.ForeignKey(
                        name: "fk_indicators_standards_standard_id",
                        column: x => x.standard_id,
                        principalSchema: "kpi",
                        principalTable: "standards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "structure_members",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    college_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    employer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decision_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_structure_members", x => x.id);
                    table.CheckConstraint("ck_members_period", "end_date IS NULL OR end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_structure_members_structure_positions_position_id",
                        column: x => x.position_id,
                        principalSchema: "org",
                        principalTable: "structure_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_structure_members_units_college_id",
                        column: x => x.college_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_structure_members_units_department_id",
                        column: x => x.department_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indicator_versions",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name_en = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    alternate_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    definition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    exclusions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    numerator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    denominator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    aggregation = table.Column<int>(type: "integer", nullable: false),
                    aggregation_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    data_source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    owner_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_required = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source_pages = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    change_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    edited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    return_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicator_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_indicator_versions_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_indicator_versions_units_owner_unit_id",
                        column: x => x.owner_unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "targets",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    numerator = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    denominator = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_targets", x => x.id);
                    table.CheckConstraint("ck_targets_year", "year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "fk_targets_indicators_indicator_id",
                        column: x => x.indicator_id,
                        principalSchema: "kpi",
                        principalTable: "indicators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_targets_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "org",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "target_revisions",
                schema: "kpi",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    old_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    new_value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    new_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    decision_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_target_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_target_revisions_targets_target_id",
                        column: x => x.target_id,
                        principalSchema: "kpi",
                        principalTable: "targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "iam",
                table: "access_state",
                columns: new[] { "id", "version" },
                values: new object[] { 1, 1L });

            migrationBuilder.CreateIndex(
                name: "ix_chain_checkpoints_chain_seq",
                schema: "logs",
                table: "chain_checkpoints",
                columns: new[] { "chain", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_conflicts_code",
                schema: "kpi",
                table: "conflicts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conflicts_status",
                schema: "kpi",
                table: "conflicts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_versions_indicator_id_number",
                schema: "kpi",
                table: "indicator_versions",
                columns: new[] { "indicator_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_indicator_versions_owner_unit_id",
                schema: "kpi",
                table: "indicator_versions",
                column: "owner_unit_id");

            migrationBuilder.CreateIndex(
                name: "ux_indicator_versions_one_working",
                schema: "kpi",
                table: "indicator_versions",
                column: "indicator_id",
                unique: true,
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_indicators_code",
                schema: "kpi",
                table: "indicators",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_indicators_standard_id",
                schema: "kpi",
                table: "indicators",
                column: "standard_id");

            migrationBuilder.CreateIndex(
                name: "ix_industry_partners_college_id",
                schema: "org",
                table: "industry_partners",
                column: "college_id");

            migrationBuilder.CreateIndex(
                name: "ix_industry_partners_department_id",
                schema: "org",
                table: "industry_partners",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_mail_outbox_batch_id",
                schema: "adm",
                table: "mail_outbox",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mail_outbox_beneficiary_id",
                schema: "adm",
                table: "mail_outbox",
                column: "beneficiary_id");

            migrationBuilder.CreateIndex(
                name: "ix_mail_outbox_created_at",
                schema: "adm",
                table: "mail_outbox",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_mail_outbox_kind_created_at",
                schema: "adm",
                table: "mail_outbox",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_outbox_pending",
                schema: "adm",
                table: "mail_outbox",
                column: "next_attempt_at",
                filter: "sent_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_mail_outbox_dedup_key",
                schema: "adm",
                table: "mail_outbox",
                column: "dedup_key",
                unique: true,
                filter: "dedup_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_mail_outbox_resent_from_id",
                schema: "adm",
                table: "mail_outbox",
                column: "resent_from_id",
                unique: true,
                filter: "resent_from_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_event_id",
                schema: "logs",
                table: "outbox",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unsent",
                schema: "logs",
                table: "outbox",
                column: "id",
                filter: "sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pillars_code",
                schema: "kpi",
                table: "pillars",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rbac_changes_role_id_status",
                schema: "iam",
                table: "rbac_changes",
                columns: new[] { "role_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_rbac_changes_one_pending",
                schema: "iam",
                table: "rbac_changes",
                column: "role_id",
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                schema: "iam",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                schema: "iam",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                schema: "iam",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_standards_code",
                schema: "kpi",
                table: "standards",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_standards_pillar_id",
                schema: "kpi",
                table: "standards",
                column: "pillar_id");

            migrationBuilder.CreateIndex(
                name: "ix_structure_members_college_id_position_id",
                schema: "org",
                table: "structure_members",
                columns: new[] { "college_id", "position_id" });

            migrationBuilder.CreateIndex(
                name: "ix_structure_members_department_id",
                schema: "org",
                table: "structure_members",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_structure_members_position_id",
                schema: "org",
                table: "structure_members",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_structure_positions_unit_id",
                schema: "org",
                table: "structure_positions",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_structure_units_code",
                schema: "org",
                table: "structure_units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_target_revisions_target_id",
                schema: "kpi",
                table: "target_revisions",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_targets_indicator_id_unit_id_year_kind",
                schema: "kpi",
                table: "targets",
                columns: new[] { "indicator_id", "unit_id", "year", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_targets_unit_id",
                schema: "kpi",
                table: "targets",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_changes_unit_id_effective_on",
                schema: "org",
                table: "unit_changes",
                columns: new[] { "unit_id", "effective_on" });

            migrationBuilder.CreateIndex(
                name: "ix_units_code",
                schema: "org",
                table: "units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_units_merged_into_id",
                schema: "org",
                table: "units",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_parent_id",
                schema: "org",
                table: "units",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_path",
                schema: "org",
                table: "units",
                column: "path");

            migrationBuilder.CreateIndex(
                name: "ix_user_overrides_user_id",
                schema: "iam",
                table: "user_overrides",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "iam",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id",
                schema: "iam",
                table: "user_roles",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "iam",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_state",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "chain_anchors",
                schema: "logs");

            migrationBuilder.DropTable(
                name: "chain_checkpoints",
                schema: "logs");

            migrationBuilder.DropTable(
                name: "chain_state",
                schema: "logs");

            migrationBuilder.DropTable(
                name: "conflicts",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "holidays",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "indicator_versions",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "industry_partners",
                schema: "org");

            migrationBuilder.DropTable(
                name: "mail_outbox",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "message_templates",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "logs");

            migrationBuilder.DropTable(
                name: "rbac_changes",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "role_grants",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "structure_members",
                schema: "org");

            migrationBuilder.DropTable(
                name: "target_revisions",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "unit_changes",
                schema: "org");

            migrationBuilder.DropTable(
                name: "user_overrides",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "structure_positions",
                schema: "org");

            migrationBuilder.DropTable(
                name: "targets",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "users",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "structure_units",
                schema: "org");

            migrationBuilder.DropTable(
                name: "indicators",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "units",
                schema: "org");

            migrationBuilder.DropTable(
                name: "standards",
                schema: "kpi");

            migrationBuilder.DropTable(
                name: "pillars",
                schema: "kpi");
        }
    }
}
