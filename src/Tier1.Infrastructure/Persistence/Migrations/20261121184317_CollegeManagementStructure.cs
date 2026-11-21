using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tier1.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0017: the college structure template on existing databases, as reference-seed.json gives new ones.
    /// - The head card «عميد الكلية» becomes «إدارة الكلية»: the dean and the three vice deans (graduate studies and research,
    ///   education and students, community service and environment), each a single required position.
    /// - «مركز التوظيف والتدريب ومتابعة الخريجين» becomes «… والجودة» with a single required «مسئول الجودة بالكلية».
    /// Their roles are created by the super administrator, so no suggested role. Members already in the template keep their
    /// positions (the dean's position is the same row). Every step finds its row by code and title, so it runs once.
    /// </summary>
    public partial class CollegeManagementStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE org.structure_units SET name_ar = 'إدارة الكلية' WHERE code = 'DEAN';

                INSERT INTO org.structure_positions (id, unit_id, title_ar, single, required, suggested_role_code, sort_order)
                SELECT gen_random_uuid(), u.id, p.title, true, true, NULL, p.sort_order
                FROM org.structure_units u
                CROSS JOIN (VALUES ('وكيل الكلية للدراسات العليا والبحوث', 2),
                                   ('وكيل الكلية لشئون التعليم والطلاب', 3),
                                   ('وكيل الكلية لشئون خدمة المجتمع وتنمية البيئة', 4)) AS p(title, sort_order)
                WHERE u.code = 'DEAN'
                  AND NOT EXISTS (SELECT 1 FROM org.structure_positions x WHERE x.unit_id = u.id AND x.title_ar = p.title);

                UPDATE org.structure_units
                SET name_ar = 'مركز التوظيف والتدريب ومتابعة الخريجين والجودة',
                    duties = CASE WHEN 'متابعة الجودة والاعتماد الأكاديمي بالكلية' = ANY (duties) THEN duties
                                  ELSE array_append(duties, 'متابعة الجودة والاعتماد الأكاديمي بالكلية') END
                WHERE code = 'CAREERS';

                -- The quality officer comes right after the centre's director; the members' row moves one place down.
                UPDATE org.structure_positions p SET sort_order = p.sort_order + 1
                FROM org.structure_units u
                WHERE p.unit_id = u.id AND u.code = 'CAREERS' AND p.sort_order >= 2
                  AND NOT EXISTS (SELECT 1 FROM org.structure_positions x WHERE x.unit_id = u.id AND x.title_ar = 'مسئول الجودة بالكلية');

                INSERT INTO org.structure_positions (id, unit_id, title_ar, single, required, suggested_role_code, sort_order)
                SELECT gen_random_uuid(), u.id, 'مسئول الجودة بالكلية', true, true, NULL, 2
                FROM org.structure_units u
                WHERE u.code = 'CAREERS'
                  AND NOT EXISTS (SELECT 1 FROM org.structure_positions x WHERE x.unit_id = u.id AND x.title_ar = 'مسئول الجودة بالكلية');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The new positions go only while nobody was ever appointed to them (members keep their history).
            migrationBuilder.Sql("""
                DELETE FROM org.structure_positions p
                WHERE p.title_ar IN ('وكيل الكلية للدراسات العليا والبحوث', 'وكيل الكلية لشئون التعليم والطلاب',
                                     'وكيل الكلية لشئون خدمة المجتمع وتنمية البيئة', 'مسئول الجودة بالكلية')
                  AND NOT EXISTS (SELECT 1 FROM org.structure_members m WHERE m.position_id = p.id);
                UPDATE org.structure_positions p SET sort_order = p.sort_order - 1
                FROM org.structure_units u
                WHERE p.unit_id = u.id AND u.code = 'CAREERS' AND p.sort_order >= 3;
                UPDATE org.structure_units SET name_ar = 'عميد الكلية' WHERE code = 'DEAN';
                UPDATE org.structure_units
                SET name_ar = 'مركز التوظيف والتدريب ومتابعة الخريجين',
                    duties = array_remove(duties, 'متابعة الجودة والاعتماد الأكاديمي بالكلية')
                WHERE code = 'CAREERS';
                """);
        }
    }
}
