# Tier 1 frontend enum reference

The values below are the exact strings used by the frontend. The labels are the Arabic text displayed in the UI. Labels can vary by screen when noted.

## Data collection and approvals

| Type | Value → Arabic label |
| --- | --- |
| `CycleStatus` | `Draft` → قيد الإعداد · `Open` → مفتوحة · `Closed` → مغلقة |
| `ApprovalStep` | `Entry` → إدخال البيانات · `DepartmentReview` → ممثل Tier 1 بالقسم · `CollegeCommittee` → لجنة Tier 1 بالكلية · `Dean` → عميد الكلية · `Quality` → الجودة المركزية · `Done` → معتمد |
| `SubmissionStatus` | `NotStarted` → لم يبدأ · `InProgress` → قيد الإدخال · `InReview` → قيد المراجعة · `Returned` → معاد للتصحيح · `Approved` → معتمد |
| `SubmissionAction` | `Submitted` → أرسل للمراجعة · `Approved` → اعتمد · `Returned` → أعيد للتصحيح · `Reopened` → أعيد فتحه |
| `ValueKind` | `Number` → قيمة · `NotAvailable` → غير متاح · `NotApplicable` → لا ينطبق |
| `IssueSeverity` | `Error` · `Warning`. The UI displays the individual validation message. |

[Collection API types](../web/src/api/collection.ts) · [Approval step names](../src/Tier1.Application/Collection/ApprovalRules.cs) · [Form event labels](../web/src/features/forms/FormPage.tsx) · [Value options](../web/src/features/forms/ValueRow.tsx)

## Indicators, targets, and organization

| Type | Value → Arabic label |
| --- | --- |
| `DefinitionStatus` | `Draft` → مسودة · `UnderReview` → قيد الاعتماد · `Approved` → معتمد · `Superseded` → حل محله إصدار أحدث |
| `Direction` | `Undefined` → غير محدد · `HigherIsBetter` → الأعلى أفضل · `LowerIsBetter` → الأقل أفضل · `Range` → الوصول إلى قيمة محددة (الأقرب أفضل) |
| `Aggregation` | `Undefined` → غير محددة · `Sum` → مجموع قيم الجهات · `RatioOfSums` → نسبة مجموع البسط إلى مجموع المقام · `DistinctCount` → عدد مميز بعد إزالة التكرار · `WeightedAverage` → متوسط مرجح · `Central` → قياس مركزي على مستوى الجامعة · `StatusOnly` → حالة لكل جهة (لا تجمع) |
| `Accumulation` | `Snapshot` → غير تراكمي (لحظي) · `Cumulative` → تراكمي |
| `CollectionLevel` | `Department` → القسم العلمي · `College` → الكلية · `University` → الجامعة |
| `Frequency` | `Monthly` → شهري · `Quarterly` → ربع سنوي · `SemiAnnual` → نصف سنوي · `Annual` → سنوي · `Custom` → مخصص |
| `TargetKind` | `Baseline` → خط الأساس · `Target` → مستهدف · `EachCollege` → مستهدف موحد لكل كلية |
| `TargetStatus` | `Proposed` → مقترح · `Approved` → معتمد |
| `TargetSource` | `None` → empty label · `Unit` → مستهدف الجهة · `EachCollege` → المستهدف الموحد لكل كلية · `University` → مستهدف الجامعة |
| `DistributionStatus` | `Covers` → التوزيع يحقق مستهدف الجامعة · `Short` → التوزيع لا يكفي · `Incomplete` → التوزيع غير مكتمل · `NotComparable` → لا يقارن بالجمع · `NoUniversityTarget` → لا يوجد مستهدف للجامعة |
| `ConflictSeverity` | `Low` → منخفضة · `Medium` → متوسطة · `High` → عالية |
| `ConflictStatus` | `Open` → مفتوح · `Ready` → جاهز للجنة · `Resolved` → محسوم · `Deferred` → مؤجل |
| `InitiativeStatus` | `Planned` → مخططة · `InProgress` → قيد التنفيذ · `Completed` → مكتملة · `Stopped` → متوقفة |
| `OrgUnitKind` | `University` → الجامعة · `College` → كلية · `Institute` → معهد · `Department` → قسم علمي · `Center` → مركز · `Administration` → إدارة · `Sector` → قطاع |

[Shared UI labels](../web/src/shared/labels.ts) · [Indicator definition options](../web/src/features/indicators/DefinitionDrawer.tsx) · [Target status labels](../web/src/features/indicators/TargetsPanel.tsx)

## Results and scorecard

| Type | Value → Arabic label or behavior |
| --- | --- |
| `Performance` | `Achieved` → محقق · `Near` → قريب من المستهدف · `Behind` → متأخر · `NoTarget` → بلا مستهدف · `NoData` → لا بيانات معتمدة · `NotJudged` → لا يحكم عليه · `NotStarted` → لم يبدأ مستهدفه · `Incomplete` → بيانات جزئية |
| `ReviewState` | `NoData` → لا قيم معتمدة · `Incomplete` → لم تكتمل الكليات · `Ready` → جاهز للنقل · `Transferred` → منقول · `Changed` → تغير بعد النقل |
| `Level` | `NoData` → لا قيمة بعد · `InLevel` → في المستوى · `Below` → دون المستوى |
| `Rag` | `Strong` → قوي / على المسار · `Developing` → قيد التطوير · `AtRisk` → معرض للخطر |
| `TierAlert` | `Balanced` → لا تفاوت كبير بين الركائز. · `Warning` → message naming the pillar one tier below · `Downgraded` → message naming the pillar that lowered the overall tier |

[Performance labels](../web/src/shared/labels.ts) · [College review labels](../web/src/features/review/ReviewPage.tsx) · [Scorecard labels and alerts](../web/src/features/scorecard/ScorecardPage.tsx)

## Permissions

| Type or field | Value → Arabic label or meaning |
| --- | --- |
| `CapLetter` | `V` → عرض · `P` → طباعة · `E` → تعديل · `A` → إضافة · `D` → حذف |
| `ScopeCode` | `A` → All · `U` → Unit · `S` → Assigned · `O` → Own |
| Override `effect` | `Allow` → منح · `Deny` → منع |
| `ChangeStatus` | `Pending` → ينتظر الاعتماد · `Approved` → معتمد وساري · `Rejected` → مرفوض · `Cancelled` → مسحوب · `Stale` → تجاوزه تعديل أحدث |

TypeScript uses `ChangeStatus` for the backend enum `RbacChangeStatus`. `OrgUnitChangeKind` is defined in the backend and has no matching declaration in the frontend API types.

[Permission API types](../web/src/api/types.ts) · [Permission change labels](../web/src/features/matrix/ChangesPage.tsx) · [Override labels](../web/src/features/users/UserDrawer.tsx) · [Organization model](../src/Tier1.Domain/Organization/Organization.cs)
